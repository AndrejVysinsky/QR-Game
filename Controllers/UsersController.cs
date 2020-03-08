﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using QuizWebApp.Data;
using QuizWebApp.Models;
using QuizWebApp.ViewModels;

namespace QuizWebApp.Controllers
{
    [Authorize]
    public class UsersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly string _userId;
        private readonly IWebHostEnvironment _hostEnvironment;
        private readonly UserManager<ApplicationUser> _userManager;

        public UsersController(IHttpContextAccessor httpContextAccessor, ApplicationDbContext context, 
                                IWebHostEnvironment hostEnvironment, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userId = httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier).Value;
            _hostEnvironment = hostEnvironment;
            _userManager = userManager;
        }

        public ActionResult MyContests()
        {
            var answersCount = new List<int>();
            var correctAnswersCount = new List<int>();

            var contests = _context.Contests.Include(c => c.ContestQuestions).ToList();
            foreach (var contest in contests)
            {
                var id = contest.Id;
                var contestQuestionUser = _context.ContestQuestionUsers
                                                        .Include(cqu => cqu.ContestQuestion)
                                                        .Where(cqu => cqu.ContestQuestion.ContestId == contest.Id)
                                                        .Where(cqu => cqu.UserId == _userId)
                                                        .ToList();

                answersCount.Add(contestQuestionUser.Count);

                int count = 0;
                foreach (var answer in contestQuestionUser)
                {
                    if (answer.IsAnsweredCorrectly)
                        count++;
                }
                correctAnswersCount.Add(count);
            }

            var userViewModel = new UserViewModel()
            {
                Contests = contests,
                AnswersCount = answersCount,
                CorrectAnswersCount = correctAnswersCount
            };

            return View(userViewModel);
        }

        [Authorize(Roles = "Admin,Moderator")]
        public ActionResult Answers()
        {
            //treba zoznam užívateľov a zoznam ich odpovedí + názvy súťaží + názov otázky

            var contestQuestionUsers = _context.ContestQuestionUsers
                                                    .Include(cqu => cqu.ContestQuestion)
                                                    .Include(cqu => cqu.ApplicationUser)
                                                    .Include(cqu => cqu.ContestQuestion.Contest)
                                                    .Include(cqu => cqu.ContestQuestion.Question)
                                                    .ToList();

            var contests = _context.Contests.ToList();
            var users = _context.ApplicationUsers.ToList();

            var answersViewModel = new AnswersViewModel()
            {
                ContestQuestionUsers = contestQuestionUsers,
                ContestList = contests,
                UserList = users
            };

            return View(answersViewModel);
        }

        [Authorize(Roles = "Admin,Moderator")]
        [HttpPost]
        public ActionResult FilterAnswers(AnswersViewModel viewModel, int? answerToDelete)
        {
            var userId = viewModel.SelectedUserId;
            var contestId = viewModel.SelectedContestId;

            var contestQuestionUsers = _context.ContestQuestionUsers
                                                    .Include(cqu => cqu.ContestQuestion)
                                                    .Include(cqu => cqu.ApplicationUser)
                                                    .Include(cqu => cqu.ContestQuestion.Contest)
                                                    .Include(cqu => cqu.ContestQuestion.Question)
                                                    .ToList();

            if (userId == null && contestId == 0)
            {
                contestQuestionUsers = contestQuestionUsers.ToList();
            }
            else if (userId == null && contestId != 0)
            {
                contestQuestionUsers = contestQuestionUsers.Where(cqu => cqu.ContestQuestion.ContestId == contestId)
                                                            .ToList();
            }
            else if (userId != null && contestId == 0)
            {
                contestQuestionUsers = contestQuestionUsers.Where(cqu => cqu.UserId == userId)
                                                            .ToList();
            }
            else
            {
                contestQuestionUsers = contestQuestionUsers.Where(cqu => cqu.ContestQuestion.ContestId == contestId)
                                                            .Where(cqu => cqu.UserId == userId)
                                                            .ToList();
            }

            //vymazanie
            if (answerToDelete != null)
            {
                int index = (int)answerToDelete;

                ContestQuestionUser cqu = contestQuestionUsers[index];
                contestQuestionUsers.RemoveAt(index);
                _context.Remove(cqu);
                _context.SaveChanges();
            }
            
            var contests = _context.Contests.ToList();
            var users = _context.ApplicationUsers.ToList();

            var answersViewModel = new AnswersViewModel()
            {
                ContestQuestionUsers = contestQuestionUsers,
                ContestList = contests,
                UserList = users
            };

            return View("Answers", answersViewModel);
        }

        [Authorize(Roles = "Admin,Moderator")]
        public async Task<IActionResult> OnPostExport(AnswersViewModel viewModel)
        {
            var contestList = new List<Contest>();
            var userList = new List<ApplicationUser>();

            if (viewModel.SelectedUserId != null)
            {
                //zvolený user
                userList.Add(_context.ApplicationUsers.SingleOrDefault(u => u.Id == viewModel.SelectedUserId));
            } 
            else
            {
                //všetci
                userList = _context.ApplicationUsers.ToList();
            }

            if (viewModel.SelectedContestId != 0)
            {
                //jedna súťaž
                contestList.Add(_context.Contests.SingleOrDefault(c => c.Id == viewModel.SelectedContestId));

                var cq = _context.ContestQuestions.Include(cq => cq.Question)
                                                    .Where(cq => cq.ContestId == viewModel.SelectedContestId)
                                                    .OrderBy(cq => cq.QuestionNumber)
                                                    .ToList();

                contestList[0].ContestQuestions = cq;
            }
            else
            {
                //všetky
                contestList = _context.Contests.ToList();

                foreach (var c in contestList)
                {
                    var cq = _context.ContestQuestions.Include(cq => cq.Question)
                                                    .Where(cq => cq.ContestId == c.Id)
                                                    .OrderBy(cq => cq.QuestionNumber)
                                                    .ToList();

                    c.ContestQuestions = cq;
                }
            }


            string sWebRootFolder = _hostEnvironment.WebRootPath;
            string sFileName = @"Odpovede.xlsx";
            string URL = string.Format("{0}://{1}/{2}", Request.Scheme, Request.Host, sFileName);
            FileInfo file = new FileInfo(Path.Combine(sWebRootFolder, sFileName));
            var memory = new MemoryStream();
            using (var fs = new FileStream(Path.Combine(sWebRootFolder, sFileName), FileMode.Create, FileAccess.Write))
            {
                IWorkbook workbook;
                workbook = new XSSFWorkbook();
                // ISheet excelSheet = workbook.CreateSheet("Demo");
                //IRow row = excelSheet.CreateRow(0);
                ISheet excelSheet;
                IRow row;

                foreach (var contest in contestList)
                {
                    excelSheet = workbook.CreateSheet(contest.Name);
                    row = excelSheet.CreateRow(0);
                    
                    row.CreateCell(0).SetCellValue("");
                    int i = 1;
                    foreach (var contestQuestion in contest.ContestQuestions)
                    {
                        row.CreateCell(i).SetCellValue(contestQuestion.QuestionNumber);
                        i++;
                    }
                    row = excelSheet.CreateRow(1);

                    row.CreateCell(0).SetCellValue("");
                    i = 1;
                    foreach (var contestQuestion in contest.ContestQuestions)
                    {
                        row.CreateCell(i).SetCellValue(contestQuestion.Question.Name);
                        i++;
                    }
                    row.CreateCell(i).SetCellValue("Počet správnych odpovedí");

                    row = excelSheet.CreateRow(2);

                    //-----------------------------------
                    int rowNumber = 3;
                    
                    foreach (var user in userList)
                    {
                        row.CreateCell(0).SetCellValue(user.Email);
                        
                        var userAnswers = _context.ContestQuestionUsers
                                                    .Include(cqu => cqu.ApplicationUser)
                                                    .Where(cqu => cqu.ApplicationUser.Id == user.Id)
                                                    .Include(cqu => cqu.ContestQuestion)
                                                    .Where(cqu => cqu.ContestQuestion.ContestId == contest.Id)
                                                    .OrderBy(cqu => cqu.ContestQuestion.QuestionNumber)
                                                    .ToList();

                        i = 1;

                        int correctCount = 0;
                        int b = 0;
                        for (int a = 0; a < contest.ContestQuestions.Count; a++)
                        {
                            if (b < userAnswers.Count && contest.ContestQuestions[a].Id == userAnswers[b].ContestQuestionId)
                            {
                                if (userAnswers[b].IsAnsweredCorrectly)
                                {
                                    row.CreateCell(i).SetCellValue("Áno");
                                    correctCount++;
                                } 
                                else
                                {
                                    row.CreateCell(i).SetCellValue("Nie");
                                }
                                b++;
                            } 
                            else 
                            {
                                row.CreateCell(i).SetCellValue("");
                            }
                            i++;
                        }
                        
                        row.CreateCell(i).SetCellValue(correctCount);

                        if (userAnswers.Count < contest.ContestQuestions.Count)
                        { 
                            if (userAnswers.Count == 0)
                                row.CreateCell(i + 1).SetCellValue("Nezúčastnil sa");
                            else
                                row.CreateCell(i + 1).SetCellValue("Nedokončil");
                        }                                                 

                        row = excelSheet.CreateRow(rowNumber);
                        rowNumber++;
                    }

                }

                /*
                    ak existuje filter podla sútaže sprav iba jeden Sheet
                    ak neexistuje sprav samostatný sheet pre každú súťaž

                    ak existuje filter podla užívateľa zapíš len jedno otázky pre každú súťaž
                    ak neexistuje vypíš všetkých

                    ak existujú oba filtre tak vypíš jeden hárok (jedna súťaž) pre jedného užívateľa
                 */

                workbook.Write(fs);
            }
            using (var stream = new FileStream(Path.Combine(sWebRootFolder, sFileName), FileMode.Open))
            {
                await stream.CopyToAsync(memory);
            }
            memory.Position = 0;
            return File(memory, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", sFileName);
        }


        [HttpGet("q/{id}")]
        public ActionResult QuestionForm(Guid id)
        {
            var contestQuestion = _context.ContestQuestions.Include(cq => cq.Contest).Include(cq => cq.Question.Answers).SingleOrDefault(cq => cq.Id == id);

            if (contestQuestion == null)
                return NotFound();

            //ak sutaz nie je aktivna, nezobrazuj otázku
            if (!contestQuestion.Contest.IsActive)
                return RedirectToAction("ErrorPage", "Home", new { chybovaHlaska = "Je nám ľúto, ale táto súťaž momentálne nie je aktívna." });

            //ak uz uzivatel na otazku odpovedal
            var UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var contestQuestionUser = _context.ContestQuestionUsers
                                                .Where(cqu => cqu.UserId == UserId)
                                                .Where(cqu => cqu.ContestQuestionId == id)
                                                .SingleOrDefault();

            if (contestQuestionUser != null)
                return RedirectToAction("ErrorPage", "Home", new { chybovaHlaska = "Na túto otázku ste už raz odpovedali. Skúste nájsť a naskenovať ďalšiu." });

            return View(contestQuestion);
        }

        [HttpPost("q/{id}")]
        public ActionResult QuestionForm(ContestQuestion contestQuestion)
        {
            var contestQuestionDb = _context.ContestQuestions
                                            .Include(cq => cq.Question)
                                            .Include(cq => cq.ContestQuestionUsers)
                                            .SingleOrDefault(cq => cq.Id == contestQuestion.Id);

            var questionDb = _context.Questions.Include(q => q.Answers).SingleOrDefault(q => q.Id == contestQuestionDb.Question.Id);

            if (contestQuestionDb.ContestQuestionUsers == null)
                contestQuestionDb.ContestQuestionUsers = new List<ContestQuestionUser>();

            int radioIndex = Int32.Parse(Request.Form["UserAnswer"]);

            contestQuestionDb.ContestQuestionUsers.Add(new ContestQuestionUser()
            {
                UserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                ContestQuestion = contestQuestionDb,
                IsAnsweredCorrectly = questionDb.Answers[radioIndex].IsCorrect
            });

            _context.SaveChanges();

            return RedirectToAction("MyContests");
        }


        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> UserList()
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            var moderators = await _userManager.GetUsersInRoleAsync("Moderator");
            var users = await _userManager.GetUsersInRoleAsync("User");
            users = users.OrderBy(user => user.IsTemporary).ToList();

            var userListViewModel = new UserListViewModel
            {
                UserList = admins.Concat(moderators).Concat(users).ToList(),
                RoleNames = new List<string> { "Administrátori", "Moderátori", "Používatelia" },
                RolesCount = new List<int> { admins.Count, moderators.Count, users.Count }
            };

            return View(userListViewModel);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public ActionResult RemoveExpiredUsers()
        {
            var expiredUsers = _context.ApplicationUsers.Where(user => user.IsTemporary == true && (DateTime.Now - user.RegistrationDate).Value.Days >= 14);
            _context.RemoveRange(expiredUsers);
            _context.SaveChanges();
            return RedirectToAction("UserList");
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public ActionResult RemoveTemporaryUsers()
        {
            var temporaryUsers = _context.ApplicationUsers.Where(user => user.IsTemporary == true).ToList();
            _context.RemoveRange(temporaryUsers);
            _context.SaveChanges();
            return RedirectToAction("UserList");
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> PromoteUser(string id)
        {
            var user = _context.ApplicationUsers.SingleOrDefault(u => u.Id == id);

            bool isModerator = await _userManager.IsInRoleAsync(user, "Moderator");
            bool isUser = await _userManager.IsInRoleAsync(user, "User");

            string newRole = string.Empty;
            string oldRole = string.Empty;

            if (isModerator)
            {
                oldRole = "Moderator";
                newRole = "Admin";
            }

            if (isUser)
            {
                oldRole = "User";
                newRole = "Moderator";
            }

            if (!string.IsNullOrEmpty(newRole))
            {
                await _userManager.RemoveFromRoleAsync(user, oldRole);
                await _userManager.AddToRoleAsync(user, newRole);
            }
            
            return RedirectToAction("UserList");
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> DemoteUser(string id)
        {
            var user = _context.ApplicationUsers.SingleOrDefault(u => u.Id == id);

            bool isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            bool isModerator = await _userManager.IsInRoleAsync(user, "Moderator");

            string oldRole = string.Empty;
            string newRole = string.Empty;

            if (isAdmin)
            {
                oldRole = "Admin";
                newRole = "Moderator";
            }

            if (isModerator)
            {
                oldRole = "Moderator";
                newRole = "User";
            }

            if (!string.IsNullOrEmpty(newRole))
            {
                await _userManager.RemoveFromRoleAsync(user, oldRole);
                await _userManager.AddToRoleAsync(user, newRole);
            }

            return RedirectToAction("UserList");
        }
    }
}