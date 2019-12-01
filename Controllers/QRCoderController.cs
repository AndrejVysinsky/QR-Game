﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using QuizWebApp.Data;
using QuizWebApp.ViewModels;

namespace QuizWebApp.Controllers
{
    [Authorize(Roles = "Admin")]
    public class QRCoderController : Controller
    {
        private readonly ApplicationDbContext _context;

        public QRCoderController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult ViewQRCodes(List<string> urls, int contestId)
        {
            QRCodeGenerator qrGenerator = new QRCodeGenerator();

            List<Byte[]> qrByteData = new List<Byte[]>();

            for (int i = 0; i < urls.Count; i++)
            {
                QRCodeData qrCodeData = qrGenerator.CreateQrCode(urls[i], QRCodeGenerator.ECCLevel.M);

                QRCode qrCode = new QRCode(qrCodeData);
                Bitmap qrCodeImage = qrCode.GetGraphic(20);

                qrByteData.Add(BitmapToBytes(qrCodeImage));
            }

            var contest = _context.Contests.SingleOrDefault(c => c.Id == contestId);
            var qrOutputViewModel = new QROutputViewModel()
            {
                ContestName = contest.Name,
                qrByteData = qrByteData
            };

            return View(qrOutputViewModel);
        }
        
        private static Byte[] BitmapToBytes(Bitmap img)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                img.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return stream.ToArray();
            }
        }
    }
}