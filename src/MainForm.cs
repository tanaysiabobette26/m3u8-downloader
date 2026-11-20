using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace M3u8Downloader
{
    public class MainForm : Form
    {
        static readonly Color Bg      = Color.FromArgb(243, 245, 249);
        static readonly Color CardBg  = Color.White;
        static readonly Color HeaderA = Color.FromArgb(37, 99, 235);   // blue
        static readonly Color HeaderB = Color.FromArgb(124, 58, 237);  // violet
        static readonly Color Accent  = Color.FromArgb(37, 99, 235);
        static readonly Color AccentHi = Color.FromArgb(29, 78, 216);
        static readonly Color Danger  = Color.FromArgb(220, 38, 38);
        static readonly Color Ink     = Color.FromArgb(15, 23, 42);
        static readonly Color Muted   = Color.FromArgb(100, 116, 139);
        static readonly Color LabelCl = Color.FromArgb(30, 41, 59);
        static readonly Color Border  = Color.FromArgb(223, 227, 234);

        TextBox txtUrl, txtName, txtFolder;
        Button btnDownload, btnBrowse, btnOpen;
        ProgressBar bar;
        Label lblStatus, lblPct;

        CancellationTokenSource cts;
        bool busy;
        string lastOutputFile;

        public MainForm()
        {
            Text = Program.AppName;
            BackColor = Bg;
            Font = new Font("Segoe UI", 10.5f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(700, 568);
            TryLoadOwnIcon();
            BuildUi();
        }

        void TryLoadOwnIcon()
        {
            try
            {
                string exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    var ico = Icon.ExtractAssociatedIcon(exe);
                    if (ico != null) Icon = ico;
                }
            }
            catch { }
        }

        void BuildUi()
        {
            // ---------- header ----------
            var header = new Panel { Dock = DockStyle.Top, Height = 138 };
            header.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (var br = new LinearGradientBrush(header.ClientRectangle, HeaderA, HeaderB, 10f))
                    g.FillRectangle(br, header.ClientRectangle);

                // white rounded badge with a download glyph
                var badge = new Rectangle(30, 38, 62, 62);
                using (var path = Rounded(badge, 16))
                using (var wb = new SolidBrush(Color.White))
                    g.FillPath(wb, path);
                DrawDownloadGlyph(g, new Rectangle(badge.X + 15, badge.Y + 13, 32, 36), Accent);

                using (var t = new Font("Segoe UI", 28f, FontStyle.Bold))
                    g.DrawString("M3U8 Downloader", t, Brushes.White, 108, 34);
                using (var st = new Font("Segoe UI", 12.5f))
                using (var sb = new SolidBrush(Color.FromArgb(222, 230, 255)))
                    g.DrawString("Paste a .m3u8 stream link — get a ready MP4 file.", st, sb, 111, 86);
            };
            Controls.Add(header);

            // ---------- card ----------
            var card = new Panel
            {
                Location = new Point(24, 158),
                Size = new Size(652, 388),
                BackColor = CardBg
            };
            card.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Border, 1f))
                using (var path = Rounded(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 14))
                    e.Graphics.DrawPath(pen, path);
            };
            Controls.Add(card);

            const int x = 30, w = 592;
            int y = 26;

            card.Controls.Add(MakeLabel("Stream link  (.m3u8 URL)", x, y, 12f, FontStyle.Bold));
            y += 30;
            txtUrl = MakeTextBox(x, y, w, 13.5f);
            txtUrl.PlaceholderText = "https://…/playlist.m3u8";
            card.Controls.Add(txtUrl);
            y += 44;

            var hint = MakeLabel("Tip: this is the direct .m3u8 stream address (copy it from the site's Network tab) — not a YouTube or page link.",
                x, y, 8.75f, FontStyle.Italic);
            hint.ForeColor = Muted;
            hint.MaximumSize = new Size(w, 0);
            card.Controls.Add(hint);
            y += 42;

            card.Controls.Add(MakeLabel("File name", x, y, 12f, FontStyle.Bold));
            card.Controls.Add(MakeLabel("Save to folder", x + 300, y, 12f, FontStyle.Bold));
            y += 30;

            txtName = MakeTextBox(x, y, 282, 12.5f);
            txtName.Text = "video";
            card.Controls.Add(txtName);

            txtFolder = MakeTextBox(x + 300, y, 250, 12.5f);
            txtFolder.Text = DefaultFolder();
            txtFolder.ReadOnly = true;
            txtFolder.BackColor = Color.FromArgb(247, 248, 251);
            card.Controls.Add(txtFolder);

            btnBrowse = new Button
            {
                Text = "…",
                Location = new Point(x + 556, y - 1),
                Size = new Size(36, 34),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold)
            };
            StyleGhostButton(btnBrowse);
            btnBrowse.Click += (s, e) => BrowseFolder();
            card.Controls.Add(btnBrowse);
            y += 56;

            btnDownload = new Button
            {
                Text = "Download MP4",
                Location = new Point(x, y),
                Size = new Size(w, 58),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Accent,
                Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnDownload.FlatAppearance.BorderSize = 0;
            btnDownload.FlatAppearance.MouseOverBackColor = AccentHi;
            btnDownload.Click += async (s, e) => await OnDownloadClick();
            card.Controls.Add(btnDownload);
            y += 74;

            bar = new ProgressBar
            {
                Location = new Point(x, y),
                Size = new Size(w, 14),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100
            };
            card.Controls.Add(bar);
            y += 22;

            lblStatus = MakeLabel("Ready.", x, y, 11f, FontStyle.Regular);
            lblStatus.ForeColor = Muted;
            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(470, 24);
            card.Controls.Add(lblStatus);

            lblPct = new Label
            {
                Text = "",
                Location = new Point(x + 500, y),
                Size = new Size(92, 24),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = CardBg,
                ForeColor = Ink,
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold)
            };
            card.Controls.Add(lblPct);
            y += 30;

            btnOpen = new Button
            {
                Text = "Open folder",
                Location = new Point(x, y),
                Size = new Size(150, 34),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Visible = false
            };
            StyleGhostButton(btnOpen);
            btnOpen.Click += (s, e) => OpenResult();
            card.Controls.Add(btnOpen);

            AcceptButton = btnDownload;
        }

        // ---------- download flow ----------
        async Task OnDownloadClick()
        {
            if (busy) { try { cts?.Cancel(); } catch { } return; }

            string url = (txtUrl.Text ?? "").Trim();
            if (url.Length == 0 || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                Info("Paste a stream link that starts with http and points to an .m3u8 playlist.");
                return;
            }
            if (LooksLikeWebPage(url))
            {
                Info("This tool downloads direct .m3u8 (HLS) stream links — not YouTube or ordinary web-page links.\n\n" +
                     "Open the video, find its .m3u8 address (e.g. in the browser's Network tab, filter \"m3u8\") and paste that here.");
                return;
            }

            string name = Sanitize((txtName.Text ?? "").Trim());
            if (name.Length == 0) name = "video";
            string folder = txtFolder.Text;
            try { Directory.CreateDirectory(folder); } catch { }

            busy = true;
            cts = new CancellationTokenSource();
            btnDownload.Text = "Cancel";
            btnDownload.BackColor = Danger;
            btnOpen.Visible = false;
            SetInputs(false);
            bar.Style = ProgressBarStyle.Marquee;   // animate immediately so it's clear something is happening
            bar.MarqueeAnimationSpeed = 30;
            lblStatus.Text = "Starting — preparing the download…";
            lblStatus.ForeColor = Muted;
            lblPct.Text = "";

            var prog = new Progress<DownloadProgress>(p =>
            {
                if (p.Percent >= 0)
                {
                    if (bar.Style != ProgressBarStyle.Continuous) bar.Style = ProgressBarStyle.Continuous;
                    int v = (int)Math.Max(0, Math.Min(100, p.Percent));
                    bar.Value = v;
                    lblPct.Text = v + "%";
                }
                if (!string.IsNullOrEmpty(p.Status))
                    lblStatus.Text = p.Status + (string.IsNullOrEmpty(p.Detail) ? "" : "   —   " + p.Detail);
            });

            DownloadResult res;
            try
            {
                res = await DownloadEngine.DownloadAsync(url, folder, name, prog, cts.Token);
            }
            catch (Exception ex)
            {
                res = new DownloadResult { Success = false, Message = ex.Message };
            }

            busy = false;
            btnDownload.Text = "Download MP4";
            btnDownload.BackColor = Accent;
            SetInputs(true);

            if (res != null && res.Success)
            {
                bar.Style = ProgressBarStyle.Continuous;
                bar.Value = 100;
                lblPct.Text = "100%";
                lblStatus.Text = "Done!  Saved to:  " + res.OutputFile;
                lblStatus.ForeColor = Color.FromArgb(21, 128, 61);
                lastOutputFile = res.OutputFile;
                btnOpen.Visible = true;
                OpenResult(); // open the folder with the new file highlighted, so it's obvious where it went
            }
            else if (res != null && res.Message == "Cancelled")
            {
                bar.Style = ProgressBarStyle.Continuous;
                bar.Value = 0;
                lblStatus.Text = "Cancelled.";
                lblStatus.ForeColor = Muted;
                lblPct.Text = "";
            }
            else
            {
                bar.Style = ProgressBarStyle.Continuous;
                bar.Value = 0;
                lblStatus.Text = "Failed — couldn't read this link.";
                lblStatus.ForeColor = Danger;
                lblPct.Text = "";
                MessageBox.Show(this,
                    "Couldn't download from this link.\n\n" +
                    "Make sure it is a direct .m3u8 (HLS) stream address — not a YouTube or web-page link — " +
                    "and that it is reachable.",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void Info(string msg)
        {
            MessageBox.Show(this, msg, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static bool LooksLikeWebPage(string url)
        {
            string u = url.ToLowerInvariant();
            string[] sites = { "youtube.com/", "youtu.be/", "vimeo.com/", "tiktok.com/",
                               "instagram.com/", "facebook.com/", "twitch.tv/" };
            foreach (var s in sites)
                if (u.Contains(s)) return true;
            return false;
        }

        void SetInputs(bool on)
        {
            txtUrl.Enabled = on;
            txtName.Enabled = on;
            btnBrowse.Enabled = on;
        }

        void BrowseFolder()
        {
            using (var d = new FolderBrowserDialog())
            {
                try { d.SelectedPath = txtFolder.Text; } catch { }
                if (d.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(d.SelectedPath))
                    txtFolder.Text = d.SelectedPath;
            }
        }

        void OpenResult()
        {
            try
            {
                if (!string.IsNullOrEmpty(lastOutputFile) && File.Exists(lastOutputFile))
                    Process.Start("explorer.exe", "/select,\"" + lastOutputFile + "\"");
                else
                    Process.Start("explorer.exe", txtFolder.Text);
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.WM_SHOWME && Program.WM_SHOWME != 0)
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                try { Activate(); NativeMethods.SetForegroundWindow(Handle); } catch { }
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy)
            {
                var r = MessageBox.Show(this, "A download is in progress. Cancel it and exit?",
                    Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.No) { e.Cancel = true; return; }
                try { cts?.Cancel(); } catch { }
            }
            base.OnFormClosing(e);
        }

        // ---------- helpers ----------
        static Label MakeLabel(string text, int x, int y, float size, FontStyle style)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                BackColor = CardBg,
                ForeColor = LabelCl,
                Font = new Font("Segoe UI", size, style)
            };
        }

        static TextBox MakeTextBox(int x, int y, int w, float size)
        {
            return new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(w, 34),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", size)
            };
        }

        static void StyleGhostButton(Button b)
        {
            b.BackColor = Color.White;
            b.ForeColor = Accent;
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 206, 214);
            b.FlatAppearance.BorderSize = 1;
            b.Cursor = Cursors.Hand;
        }

        static void DrawDownloadGlyph(Graphics g, Rectangle r, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new SolidBrush(color))
            {
                int cx = r.X + r.Width / 2;
                int shaftW = Math.Max(5, r.Width / 5);
                int shaftTop = r.Y;
                int shaftBottom = r.Y + (int)(r.Height * 0.52);
                g.FillRectangle(br, cx - shaftW / 2, shaftTop, shaftW, shaftBottom - shaftTop);
                int headW = (int)(r.Width * 0.72);
                var head = new Point[]
                {
                    new Point(cx - headW / 2, shaftBottom - 2),
                    new Point(cx + headW / 2, shaftBottom - 2),
                    new Point(cx, r.Y + (int)(r.Height * 0.82))
                };
                g.FillPolygon(br, head);
                int trayY = r.Y + (int)(r.Height * 0.90);
                g.FillRectangle(br, cx - headW / 2, trayY, headW, Math.Max(4, r.Height / 11));
            }
        }

        static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static string DefaultFolder()
        {
            try
            {
                string v = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            catch { }
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        static string Sanitize(string s)
        {
            if (s == null) return "";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }
    }
}
