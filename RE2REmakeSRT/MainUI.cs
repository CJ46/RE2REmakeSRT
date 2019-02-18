﻿using DoubleBuffered;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RE2REmakeSRT
{
    public partial class MainUI : Form
    {
        // How often to perform more expensive operations.
        // 2500 milliseconds for updating pointers.
        // 333 milliseconds for a full scan.
        // 16 milliseconds for a slim scan.
        public const long PTR_UPDATE_TICKS = TimeSpan.TicksPerMillisecond * 2500L;
        public const long FULL_UI_DRAW_TICKS = TimeSpan.TicksPerMillisecond * 333L;
        public const double SLIM_UI_DRAW_MS = 16d;

        private System.Timers.Timer memoryPollingTimer;
        private long lastPtrUpdate;
        private long lastFullUIDraw;

        // Quality settings (high performance).
        private CompositingMode compositingMode = CompositingMode.SourceOver;
        private CompositingQuality compositingQuality = CompositingQuality.HighSpeed;
        private SmoothingMode smoothingMode = SmoothingMode.None;
        private PixelOffsetMode pixelOffsetMode = PixelOffsetMode.Half;
        private InterpolationMode interpolationMode = InterpolationMode.NearestNeighbor;
        private TextRenderingHint textRenderingHint = TextRenderingHint.AntiAliasGridFit;
        
        // Text alignment and formatting.
        private StringFormat invStringFormat = new StringFormat(StringFormat.GenericDefault) { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Far };
        private StringFormat stdStringFormat = new StringFormat(StringFormat.GenericDefault) { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };

        private Bitmap inventoryError; // An error image.


        public MainUI()
        {
            InitializeComponent();

            // Set titlebar.
            this.Text += string.Format(" {0}", Program.srtVersion);

            this.ContextMenu = Program.contextMenu;
            this.playerHealthStatus.ContextMenu = Program.contextMenu;
            this.statisticsPanel.ContextMenu = Program.contextMenu;
            this.inventoryPanel.ContextMenu = Program.contextMenu;

            if (Program.programSpecialOptions.Flags.HasFlag(ProgramFlags.NoTitleBar))
                this.FormBorderStyle = FormBorderStyle.None;

            if (Program.programSpecialOptions.Flags.HasFlag(ProgramFlags.Transparent))
                this.TransparencyKey = Color.Black;

            // Only run the following code if we're rendering inventory.
            if (!Program.programSpecialOptions.Flags.HasFlag(ProgramFlags.NoInventory))
            {
                GameMemory.GenerateImages();

                // Create a black slot image for when side-pack is not equipped.
                inventoryError = new Bitmap(Program.INV_SLOT_WIDTH, Program.INV_SLOT_HEIGHT, PixelFormat.Format32bppPArgb);
                using (Graphics grp = Graphics.FromImage(inventoryError))
                {
                    grp.FillRectangle(new SolidBrush(Color.FromArgb(255, 0, 0, 0)), 0, 0, inventoryError.Width, inventoryError.Height);
                    grp.DrawLine(new Pen(Color.FromArgb(150, 255, 0, 0), 3), 0, 0, inventoryError.Width, inventoryError.Height);
                    grp.DrawLine(new Pen(Color.FromArgb(150, 255, 0, 0), 3), inventoryError.Width, 0, 0, inventoryError.Height);
                }

                // Set the width and height of the inventory display so it matches the maximum items and the scaling size of those items.
                this.inventoryPanel.Width = Program.INV_SLOT_WIDTH * 4;
                this.inventoryPanel.Height = Program.INV_SLOT_HEIGHT * 5;

                // Adjust main form width as well.
                this.Width = this.statisticsPanel.Width + 24 + this.inventoryPanel.Width;

                // Only adjust form height if its greater than 461. We don't want it to go below this size.
                if (41 + this.inventoryPanel.Height > 461)
                    this.Height = 41 + this.inventoryPanel.Height;
            }
            else
            {
                // Disable rendering of the inventory panel.
                this.inventoryPanel.Visible = false;

                // Adjust main form width as well.
                this.Width = this.statisticsPanel.Width + 2;
            }

            lastPtrUpdate = DateTime.UtcNow.Ticks;
            lastFullUIDraw = DateTime.UtcNow.Ticks;

            memoryPollingTimer = new System.Timers.Timer() { AutoReset = false, Interval = SLIM_UI_DRAW_MS };
            memoryPollingTimer.Elapsed += MemoryPollingTimer_Elapsed;
            memoryPollingTimer.Start();
        }

        private void MemoryPollingTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                // Suspend UI layout logic to perform redrawing.
                MainUI uiForm = (MainUI)Program.mainContext.MainForm;

                if (Program.programSpecialOptions.Flags.HasFlag(ProgramFlags.AlwaysOnTop))
                {
                    bool hasFocus;
                    if (uiForm.InvokeRequired)
                        hasFocus = PInvoke.HasActiveFocus((IntPtr)uiForm.Invoke(new Func<IntPtr>(() => uiForm.Handle)));
                    else
                        hasFocus = PInvoke.HasActiveFocus(uiForm.Handle);

                    if (!hasFocus)
                    {
                        if (uiForm.InvokeRequired)
                            uiForm.Invoke(new Action(() => uiForm.TopMost = true));
                        else
                            uiForm.TopMost = true;
                    }
                }

                // Only perform a pointer update occasionally.
                if (DateTime.UtcNow.Ticks - lastPtrUpdate >= PTR_UPDATE_TICKS)
                {
                    // Update the last drawn time.
                    lastPtrUpdate = DateTime.UtcNow.Ticks;

                    // Update the pointers.
                    Program.gameMem.UpdatePointers();
                }

                // Only draw occasionally, not as often as the stats panel.
                if (DateTime.UtcNow.Ticks - lastFullUIDraw >= FULL_UI_DRAW_TICKS)
                {
                    // Update the last drawn time.
                    lastFullUIDraw = DateTime.UtcNow.Ticks;

                    // Get the full amount of updated information from memory.
                    Program.gameMem.Refresh();

                    // Only draw these periodically to reduce CPU usage.
                    uiForm.playerHealthStatus.Invalidate();
                    if (!Program.programSpecialOptions.Flags.HasFlag(ProgramFlags.NoInventory))
                        uiForm.inventoryPanel.Invalidate();
                }
                else
                {
                    // Get a slimmed-down amount of updated information from memory.
                    Program.gameMem.RefreshSlim();
                }

                // Always draw this as these are simple text draws and contains the IGT/frame count.
                uiForm.statisticsPanel.Invalidate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[{0}] {1}\r\n{2}", ex.GetType().ToString(), ex.Message, ex.StackTrace);
            }
            finally
            {
                // Trigger the timer to start once again.
                ((System.Timers.Timer)sender).Start();
            }
        }

        private void playerHealthStatus_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = smoothingMode;
            e.Graphics.CompositingQuality = compositingQuality;
            e.Graphics.CompositingMode = compositingMode;
            e.Graphics.InterpolationMode = interpolationMode;
            e.Graphics.PixelOffsetMode = pixelOffsetMode;
            e.Graphics.TextRenderingHint = textRenderingHint;

            // Draw health.
            Font healthFont = new Font("Consolas", 14, FontStyle.Bold);
            if (Program.gameMem.PlayerCurrentHealth > 1200 || Program.gameMem.PlayerCurrentHealth < 0) // Dead?
            {
                e.Graphics.DrawString("DEAD", healthFont, Brushes.Red, 15, 37, stdStringFormat);
                playerHealthStatus.ThreadSafeSetHealthImage(Properties.Resources.EMPTY, "EMPTY");
            }
            else if (Program.gameMem.PlayerPoisoned)
            {
                e.Graphics.DrawString(Program.gameMem.PlayerCurrentHealth.ToString(), healthFont, Brushes.MediumPurple, 15, 37, stdStringFormat);
                playerHealthStatus.ThreadSafeSetHealthImage(Properties.Resources.POISON, "POISON");
            }
            else if (Program.gameMem.PlayerCurrentHealth >= 801) // Fine (Green)
            {
                e.Graphics.DrawString(Program.gameMem.PlayerCurrentHealth.ToString(), healthFont, Brushes.LawnGreen, 15, 37, stdStringFormat);
                playerHealthStatus.ThreadSafeSetHealthImage(Properties.Resources.FINE, "FINE");
            }
            else if (Program.gameMem.PlayerCurrentHealth <= 800 && Program.gameMem.PlayerCurrentHealth >= 361) // Caution (Yellow)
            {
                e.Graphics.DrawString(Program.gameMem.PlayerCurrentHealth.ToString(), healthFont, Brushes.Goldenrod, 15, 37, stdStringFormat);
                playerHealthStatus.ThreadSafeSetHealthImage(Properties.Resources.CAUTION_YELLOW, "CAUTION_YELLOW");
            }
            else if (Program.gameMem.PlayerCurrentHealth <= 360) // Danger (Red)
            {
                e.Graphics.DrawString(Program.gameMem.PlayerCurrentHealth.ToString(), healthFont, Brushes.Red, 15, 37, stdStringFormat);
                playerHealthStatus.ThreadSafeSetHealthImage(Properties.Resources.DANGER, "DANGER");
            }
        }

        private void playerInfoPanel_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = smoothingMode;
            e.Graphics.CompositingQuality = compositingQuality;
            e.Graphics.CompositingMode = compositingMode;
            e.Graphics.InterpolationMode = interpolationMode;
            e.Graphics.PixelOffsetMode = pixelOffsetMode;
            e.Graphics.TextRenderingHint = textRenderingHint;
        }

        private void inventoryPanel_Paint(object sender, PaintEventArgs e)
        {
            if (!Program.programSpecialOptions.Flags.HasFlag(ProgramFlags.NoInventory))
            {
                e.Graphics.SmoothingMode = smoothingMode;
                e.Graphics.CompositingQuality = compositingQuality;
                e.Graphics.CompositingMode = compositingMode;
                e.Graphics.InterpolationMode = interpolationMode;
                e.Graphics.PixelOffsetMode = pixelOffsetMode;
                e.Graphics.TextRenderingHint = textRenderingHint;

                foreach (InventoryEntry inv in Program.gameMem.PlayerInventory)
                {
                    if (inv == default || inv.SlotPosition < 0 || inv.SlotPosition > 19 || inv.IsEmptySlot)
                        continue;

                    int slotColumn = inv.SlotPosition % 4;
                    int slotRow = inv.SlotPosition / 4;
                    int imageX = slotColumn * Program.INV_SLOT_WIDTH;
                    int imageY = slotRow * Program.INV_SLOT_HEIGHT;
                    int textX = imageX + Program.INV_SLOT_WIDTH;
                    int textY = imageY + Program.INV_SLOT_HEIGHT;
                    Brush textBrush = Brushes.White;

                    if (inv.Quantity == 0)
                        textBrush = Brushes.DarkRed;

                    Rectangle imageRect;
                    TextureBrush imageBrush;
                    Weapon weapon;
                    if (inv.IsItem && GameMemory.ItemToImageTranslation.ContainsKey(inv.ItemID))
                    {
                        imageRect = GameMemory.ItemToImageTranslation[inv.ItemID];

                        if (inv.ItemID == ItemEnumeration.OldKey)
                            imageBrush = new TextureBrush(GameMemory.inventoryImagePatch1, imageRect);
                        else
                            imageBrush = new TextureBrush(GameMemory.inventoryImage, imageRect);
                    }
                    else if (inv.IsWeapon && GameMemory.WeaponToImageTranslation.ContainsKey(weapon = new Weapon() { WeaponID = inv.WeaponID, Attachments = inv.Attachments }))
                    {
                        imageRect = GameMemory.WeaponToImageTranslation[weapon];
                        imageBrush = new TextureBrush(GameMemory.inventoryImage, imageRect);
                    }
                    else
                    {
                        imageRect = new Rectangle(0, 0, Program.INV_SLOT_WIDTH, Program.INV_SLOT_HEIGHT);
                        imageBrush = new TextureBrush(inventoryError, imageRect);
                    }
                    
                    e.Graphics.FillRectangle(imageBrush, imageX, imageY, imageRect.Width, imageRect.Height);
                    e.Graphics.DrawString((inv.Quantity != -1) ? inv.Quantity.ToString() : "∞", new Font("Consolas", 14, FontStyle.Bold), textBrush, textX, textY, invStringFormat);
                }
            }
        }

        private void statisticsPanel_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = smoothingMode;
            e.Graphics.CompositingQuality = compositingQuality;
            e.Graphics.CompositingMode = compositingMode;
            e.Graphics.InterpolationMode = interpolationMode;
            e.Graphics.PixelOffsetMode = pixelOffsetMode;
            e.Graphics.TextRenderingHint = textRenderingHint;

            // Additional information and stats.
            // Adjustments for displaying text properly.
            int heightGap = 15;
            int heightOffset = 0;
            int i = 1;

            // IGT Display.
            e.Graphics.DrawString(string.Format("{0}", Program.gameMem.IGTString), new Font("Consolas", 16, FontStyle.Bold), Brushes.White, 0, 0, stdStringFormat);

            if (Program.programSpecialOptions.Flags.HasFlag(ProgramFlags.Debug))
            {
                e.Graphics.DrawString("Raw IGT", new Font("Consolas", 9, FontStyle.Bold), Brushes.Gray, 2, 25, stdStringFormat);
                e.Graphics.DrawString(Program.gameMem.IGTRaw.ToString(), new Font("Consolas", 12, FontStyle.Bold), Brushes.Gray, 0, 37, stdStringFormat);
                heightOffset = 25; // Adding an additional offset to accomdate Raw IGT.
            }

            e.Graphics.DrawString(string.Format("DA Rank: {0}", Program.gameMem.Rank), new Font("Consolas", 9, FontStyle.Bold), Brushes.Gray, 0, heightOffset + (heightGap * ++i), stdStringFormat);
            e.Graphics.DrawString(string.Format("DA Score: {0}", Program.gameMem.RankScore), new Font("Consolas", 9, FontStyle.Bold), Brushes.Gray, 0, heightOffset + (heightGap * ++i), stdStringFormat);
            
            e.Graphics.DrawString("Enemies", new Font("Consolas", 10, FontStyle.Bold), Brushes.Red, 0, heightOffset + (heightGap * ++i), stdStringFormat);
            foreach (EnemyHP enemyHP in Program.gameMem.EnemyHealth.Where(a => a.IsAlive).OrderBy(a => a.Percentage).ThenByDescending(a => a.CurrentHP))
            {
                e.Graphics.DrawString(string.Format("{0} {1:P1}", enemyHP.CurrentHP, enemyHP.Percentage), new Font("Consolas", 10, FontStyle.Bold), Brushes.Red, 0, heightOffset + (heightGap * ++i), stdStringFormat);
            }
        }

        private void inventoryPanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (!Program.programSpecialOptions.Flags.HasFlag(ProgramFlags.NoInventory))
                if (e.Button == MouseButtons.Left)
                    PInvoke.DragControl(((DoubleBufferedPanel)sender).Parent.Handle);
        }

        private void statisticsPanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                PInvoke.DragControl(((DoubleBufferedPanel)sender).Parent.Handle);
        }

        private void playerHealthStatus_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                PInvoke.DragControl(((PictureBox)sender).Parent.Handle);
        }

        private void MainUI_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                PInvoke.DragControl(((Form)sender).Handle);
        }
    }
}
