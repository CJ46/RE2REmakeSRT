﻿using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;

namespace RE2REmakeSRT
{
    public static class Program
    {
        public static ApplicationContext mainContext;
        public static ProgramFlags programSpecialOptions;
        public static Process gameProc;
        public static GameMemory gameMem;
        public static Bitmap inventoryImage; // The inventory item sheet.
        public static Bitmap inventoryError; // An error image.
        public const double INV_SLOT_SCALING = 0.75; // Scaling factor for the inventory images.
        public const int INV_SLOT_WIDTH = (int)(112 * INV_SLOT_SCALING); // Individual inventory slot width.
        public const int INV_SLOT_HEIGHT = (int)(112 * INV_SLOT_SCALING); // Individual inventory slot height.

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static void Main(string[] args)
        {
            // Handle command-line parameters.
            programSpecialOptions = ProgramFlags.None;
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--Help", StringComparison.InvariantCultureIgnoreCase))
                {
                    StringBuilder message = new StringBuilder("Command-line arguments:\r\n\r\n");
                    message.AppendFormat("{0}\r\n\t{1}\r\n\r\n", "--Skip-Checksum", "Skip the checksum file validation step.");
                    message.AppendFormat("{0}\r\n\t{1}\r\n\r\n", "--No-Titlebar", "Hide the titlebar and window frame.");
                    message.AppendFormat("{0}\r\n\t{1}\r\n\r\n", "--Always-On-Top", "Always appear on top of other windows.");
                    message.AppendFormat("{0}\r\n\t{1}\r\n\r\n", "--Debug", "Debug mode.");

                    MessageBox.Show(null, message.ToString().Trim(), string.Empty, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Environment.Exit(0);
                }

                if (string.Equals(arg, "--Skip-Checksum", StringComparison.InvariantCultureIgnoreCase))
                    programSpecialOptions |= ProgramFlags.SkipChecksumCheck;

                if (string.Equals(arg, "--No-Titlebar", StringComparison.InvariantCultureIgnoreCase))
                    programSpecialOptions |= ProgramFlags.NoTitleBar;

                if (string.Equals(arg, "--Always-On-Top", StringComparison.InvariantCultureIgnoreCase))
                    programSpecialOptions |= ProgramFlags.AlwaysOnTop;

                // Assigning here because debug will always be the sum of all of the options being on.
                if (string.Equals(arg, "--Debug", StringComparison.InvariantCultureIgnoreCase))
                    programSpecialOptions = ProgramFlags.Debug;
            }

            // Standard WinForms stuff.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Transform the inventory image in resources down from 32bpp w/ Alpha to 16bpp w/o Alpha. This greatly improve performance especially when coupled with CompositingMode.SourceCopy because no complex alpha blending needs to occur.
            inventoryImage = Properties.Resources.ui0100_iam_texout.Clone(new Rectangle(0, 0, Properties.Resources.ui0100_iam_texout.Width, Properties.Resources.ui0100_iam_texout.Height), PixelFormat.Format16bppRgb555);

            // Rescales the image down if the scaling factor is not 1.
            if (INV_SLOT_SCALING != 1d)
                inventoryImage = new Bitmap(inventoryImage, (int)(inventoryImage.Width * INV_SLOT_SCALING), (int)(inventoryImage.Height * INV_SLOT_SCALING));

            // Create a black slot image for when side-pack is not equipped.
            inventoryError = new Bitmap(INV_SLOT_WIDTH, INV_SLOT_HEIGHT, PixelFormat.Format16bppRgb555);
            using (Graphics grp = Graphics.FromImage(inventoryError))
            {
                grp.FillRectangle(new SolidBrush(Color.FromArgb(255, 0, 0, 0)), 0, 0, inventoryError.Width, inventoryError.Height);
                grp.DrawLine(new Pen(Color.FromArgb(150, 255, 0, 0), 3), 0, 0, inventoryError.Width, inventoryError.Height);
                grp.DrawLine(new Pen(Color.FromArgb(150, 255, 0, 0), 3), inventoryError.Width, 0, 0, inventoryError.Height);
            }

            // This form finds the process for re2.exe (assigned to gameProc) or waits until it is found.
            using (mainContext = new ApplicationContext(new AttachUI()))
                Application.Run(mainContext);

            // Attach to the re2.exe process now that we've found it and show the UI.
            using (gameMem = new GameMemory(gameProc))
            using (mainContext = new ApplicationContext(new MainUI()))
                Application.Run(mainContext);
        }
    }
}
