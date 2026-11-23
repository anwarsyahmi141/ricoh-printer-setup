using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;

namespace RicohPrinterSetup
{
    public class PrinterConfig
    {
        public string id { get; set; }
        public string displayName { get; set; }
        public string modelName { get; set; }
        public string ip { get; set; }
        public string infRelativePath { get; set; }
        public string location { get; set; }

        // Runtime states
        [ScriptIgnore]
        public bool IsInstalled { get; set; }
        [ScriptIgnore]
        public bool IsOnline { get; set; }
        [ScriptIgnore]
        public long PingMs { get; set; }
    }

    public class ModelProfile
    {
        public string Display { get; set; }
        public string DriverName { get; set; }
        public string InfPath { get; set; }
        public string Prefix { get; set; }

        public override string ToString()
        {
            return Display;
        }

        public static List<ModelProfile> LoadFromDriversFolder(string baseDir)
        {
            Dictionary<string, ModelProfile> profileMap = new Dictionary<string, ModelProfile>(StringComparer.OrdinalIgnoreCase);
            string driversDir = Path.Combine(baseDir, "drivers");

            if (Directory.Exists(driversDir))
            {
                try
                {
                    string[] infFiles = Directory.GetFiles(driversDir, "*.inf", SearchOption.AllDirectories);
                    foreach (string inf in infFiles)
                    {
                        try
                        {
                            string relPath = inf.Substring(baseDir.TrimEnd('\\', '/').Length + 1);
                            string[] lines = File.ReadAllLines(inf);
                            foreach (string line in lines)
                            {
                                Match m = Regex.Match(line, "^\"(RICOH [^\"]+PCL 6)\"=", RegexOptions.IgnoreCase);
                                if (!m.Success)
                                {
                                    m = Regex.Match(line, "DrvName\\w*=\"(RICOH [^\"]+PCL 6)\"", RegexOptions.IgnoreCase);
                                }
                                if (m.Success)
                                {
                                    string fullName = m.Groups[1].Value.Trim();
                                    if (!profileMap.ContainsKey(fullName))
                                    {
                                        string prefix = fullName.Replace(" PCL 6", "").Replace("RICOH ", "Ricoh ");
                                        profileMap[fullName] = new ModelProfile
                                        {
                                            Display = fullName,
                                            DriverName = fullName,
                                            InfPath = relPath,
                                            Prefix = prefix
                                        };
                                    }
                                }
                            }
                        }
                        catch {}
                    }
                }
                catch {}
            }

            List<ModelProfile> list = new List<ModelProfile>(profileMap.Values);
            list.Sort((a, b) => string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase));

            if (list.Count == 0)
            {
                return GetDefaults();
            }

            return list;
        }

        public static List<ModelProfile> GetDefaults()
        {
            return new List<ModelProfile>
            {
                new ModelProfile {
                    Display = "RICOH MP C3503 PCL 6",
                    DriverName = "RICOH MP C3503 PCL 6",
                    InfPath = @"drivers\MP C3003-C3503\disk1\MPC3003_.inf",
                    Prefix = "Ricoh MP C3503"
                },
                new ModelProfile {
                    Display = "RICOH IM C2000 PCL 6",
                    DriverName = "RICOH IM C2000 PCL 6",
                    InfPath = @"drivers\IM C2000-C2500\disk1\MPC2000_.inf",
                    Prefix = "Ricoh IM C2000"
                },
                new ModelProfile {
                    Display = "RICOH Aficio MP C2800 PCL 6",
                    DriverName = "RICOH Aficio MP C2800 PCL 6",
                    InfPath = @"drivers\MP C2800-C3300\disk1\OEMSETUP.INF",
                    Prefix = "Ricoh Aficio MP C2800"
                }
            };
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private List<PrinterConfig> printers = new List<PrinterConfig>();
        private FlowLayoutPanel flowPrinters;
        private TextBox txtLog;
        private ProgressBar progressBar;
        private Label lblStatus;
        private Button btnRefresh;
        private Button btnScan;
        private Button btnManualAdd;
        private Button btnToggleLog;
        private Panel panelLog;
        private Panel bottomContainer;
        private string configPath;

        public MainForm()
        {
            configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "printers.json");
            InitializeComponents();
            LoadConfiguration();
            RefreshAllStatusAsync();
        }

        private void InitializeComponents()
        {
            this.Text = "Ricoh Office Printer Setup";
            this.Size = new Size(1160, 760);
            this.MinimumSize = new Size(980, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Top Header Panel
            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = 90;
            headerPanel.BackColor = Color.FromArgb(26, 32, 44);

            Panel redBar = new Panel();
            redBar.Dock = DockStyle.Top;
            redBar.Height = 4;
            redBar.BackColor = Color.FromArgb(207, 16, 45); // Ricoh Red
            headerPanel.Controls.Add(redBar);

            // Left Title Info container
            Panel titlePanel = new Panel();
            titlePanel.Dock = DockStyle.Left;
            titlePanel.Width = 350;
            titlePanel.BackColor = Color.Transparent;

            Label lblTitle = new Label();
            lblTitle.Text = "RICOH Office Printer Setup";
            lblTitle.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            lblTitle.ForeColor = Color.White;
            lblTitle.Location = new Point(22, 18);
            lblTitle.AutoSize = true;
            titlePanel.Controls.Add(lblTitle);

            Label lblSubtitle = new Label();
            lblSubtitle.Text = "Self-Service 1-Click Printer Setup for Workstations";
            lblSubtitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            lblSubtitle.ForeColor = Color.FromArgb(148, 163, 184);
            lblSubtitle.Location = new Point(23, 50);
            lblSubtitle.AutoSize = true;
            titlePanel.Controls.Add(lblSubtitle);

            headerPanel.Controls.Add(titlePanel);

            // Top Right Action Buttons
            FlowLayoutPanel headerButtonsPanel = new FlowLayoutPanel();
            headerButtonsPanel.Dock = DockStyle.Right;
            headerButtonsPanel.FlowDirection = FlowDirection.LeftToRight;
            headerButtonsPanel.WrapContents = false;
            headerButtonsPanel.AutoSize = true;
            headerButtonsPanel.Padding = new Padding(0, 24, 20, 0);
            headerButtonsPanel.BackColor = Color.Transparent;

            Button btnAddressBook = CreateFlatButton("📖 Address Book", Color.FromArgb(79, 70, 229), Color.White, 150, 38);
            btnAddressBook.Margin = new Padding(0, 0, 10, 0);
            btnAddressBook.Click += (s, e) => OpenAddressBookDialog();
            headerButtonsPanel.Controls.Add(btnAddressBook);

            btnManualAdd = CreateFlatButton("➕ Add Manually (Offline)", Color.FromArgb(16, 185, 129), Color.White, 195, 38);
            btnManualAdd.Margin = new Padding(0, 0, 10, 0);
            btnManualAdd.Click += (s, e) => OpenManualAddDialog();
            headerButtonsPanel.Controls.Add(btnManualAdd);

            btnScan = CreateFlatButton("🔍 Scan Network", Color.FromArgb(207, 16, 45), Color.White, 155, 38);
            btnScan.Margin = new Padding(0, 0, 10, 0);
            btnScan.Click += (s, e) => OpenScannerDialog();
            headerButtonsPanel.Controls.Add(btnScan);

            btnRefresh = CreateFlatButton("🔄 Refresh", Color.FromArgb(51, 65, 85), Color.White, 115, 38);
            btnRefresh.Click += (s, e) => {
                LoadConfiguration();
                RefreshAllStatusAsync();
            };
            headerButtonsPanel.Controls.Add(btnRefresh);

            headerPanel.Controls.Add(headerButtonsPanel);
            this.Controls.Add(headerPanel);

            // Bottom Status & Log Panel
            bottomContainer = new Panel();
            bottomContainer.Dock = DockStyle.Bottom;
            bottomContainer.Height = 44;
            bottomContainer.BackColor = Color.White;

            Panel statusStrip = new Panel();
            statusStrip.Dock = DockStyle.Top;
            statusStrip.Height = 44;
            statusStrip.BackColor = Color.FromArgb(241, 245, 249);

            lblStatus = new Label();
            lblStatus.Text = "Ready";
            lblStatus.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblStatus.ForeColor = Color.FromArgb(51, 65, 85);
            lblStatus.Location = new Point(20, 12);
            lblStatus.AutoSize = true;
            statusStrip.Controls.Add(lblStatus);

            FlowLayoutPanel statusRightPanel = new FlowLayoutPanel();
            statusRightPanel.Dock = DockStyle.Right;
            statusRightPanel.FlowDirection = FlowDirection.RightToLeft;
            statusRightPanel.WrapContents = false;
            statusRightPanel.AutoSize = true;
            statusRightPanel.Padding = new Padding(0, 7, 20, 0);
            statusRightPanel.BackColor = Color.Transparent;

            btnToggleLog = CreateFlatButton("📋 Show Activity Log", Color.FromArgb(203, 213, 225), Color.FromArgb(30, 41, 59), 160, 28);
            btnToggleLog.Click += (s, e) => {
                bool isShowing = (bottomContainer.Height > 60);
                if (isShowing)
                {
                    bottomContainer.Height = 44;
                    btnToggleLog.Text = "📋 Show Activity Log";
                }
                else
                {
                    bottomContainer.Height = 180;
                    btnToggleLog.Text = "✖ Hide Activity Log";
                }
            };
            statusRightPanel.Controls.Add(btnToggleLog);

            progressBar = new ProgressBar();
            progressBar.Size = new Size(200, 18);
            progressBar.Margin = new Padding(0, 5, 12, 0);
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Visible = false;
            statusRightPanel.Controls.Add(progressBar);

            statusStrip.Controls.Add(statusRightPanel);
            bottomContainer.Controls.Add(statusStrip);

            panelLog = new Panel();
            panelLog.Dock = DockStyle.Fill;
            panelLog.Padding = new Padding(12);

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Dock = DockStyle.Fill;
            txtLog.BackColor = Color.FromArgb(15, 23, 42);
            txtLog.ForeColor = Color.FromArgb(226, 232, 240);
            txtLog.Font = new Font("Consolas", 9f);
            panelLog.Controls.Add(txtLog);

            bottomContainer.Controls.Add(panelLog);
            this.Controls.Add(bottomContainer);

            // Center Flow Panel for Printer Cards
            flowPrinters = new FlowLayoutPanel();
            flowPrinters.Dock = DockStyle.Fill;
            flowPrinters.AutoScroll = true;
            flowPrinters.FlowDirection = FlowDirection.TopDown;
            flowPrinters.WrapContents = false;
            flowPrinters.Padding = new Padding(20, 15, 20, 15);
            flowPrinters.BackColor = Color.FromArgb(248, 250, 252);
            this.Controls.Add(flowPrinters);
            flowPrinters.BringToFront();

            this.Resize += (s, e) => {
                UpdateCardWidths();
            };
        }

        private void UpdateCardWidths()
        {
            int cardWidth = flowPrinters.ClientSize.Width - 45;
            if (cardWidth < 850) cardWidth = 850;

            foreach (Control c in flowPrinters.Controls)
            {
                if (c is Panel)
                {
                    c.Width = cardWidth;
                }
            }
        }

        private Button CreateFlatButton(string text, Color bg, Color fg, int width, int height)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.BackColor = bg;
            btn.ForeColor = fg;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.Size = new Size(width, height);
            btn.Cursor = Cursors.Hand;
            btn.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            return btn;
        }

        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    JavaScriptSerializer js = new JavaScriptSerializer();
                    printers = js.Deserialize<List<PrinterConfig>>(json);
                    if (printers == null) printers = new List<PrinterConfig>();
                    Log("Loaded " + printers.Count + " printers from printers.json");
                }
                else
                {
                    CreateDefaultConfiguration();
                }
            }
            catch (Exception ex)
            {
                Log("Error reading printers.json: " + ex.Message + ". Falling back to defaults.");
                CreateDefaultConfiguration();
            }
        }

        private void SaveConfiguration()
        {
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                File.WriteAllText(configPath, js.Serialize(printers));
            }
            catch (Exception ex)
            {
                Log("Error saving printers.json: " + ex.Message);
            }
        }

        public void CreateDefaultConfiguration()
        {
            printers = new List<PrinterConfig>
            {
                new PrinterConfig {
                    id = "c3503_160",
                    displayName = "Ricoh MP C3503 (160)",
                    modelName = "RICOH MP C3503 PCL 6",
                    ip = "192.168.23.160",
                    infRelativePath = @"drivers\MP C3003-C3503\disk1\MPC3003_.inf",
                    location = "Main Office (192.168.23.160)"
                },
                new PrinterConfig {
                    id = "c3503_177",
                    displayName = "Ricoh MP C3503 (177)",
                    modelName = "RICOH MP C3503 PCL 6",
                    ip = "192.168.23.177",
                    infRelativePath = @"drivers\MP C3003-C3503\disk1\MPC3003_.inf",
                    location = "Office Department (192.168.23.177)"
                },
                new PrinterConfig {
                    id = "c2800_152",
                    displayName = "Ricoh Aficio MP C2800",
                    modelName = "RICOH Aficio MP C2800 PCL 6",
                    ip = "192.168.23.152",
                    infRelativePath = @"drivers\MP C2800-C3300\disk1\OEMSETUP.INF",
                    location = "HR / Accounts (192.168.23.152)"
                },
                new PrinterConfig {
                    id = "c2000_168",
                    displayName = "Ricoh IM C2000",
                    modelName = "RICOH IM C2000 PCL 6",
                    ip = "192.168.23.168",
                    infRelativePath = @"drivers\IM C2000-C2500\disk1\MPC2000_.inf",
                    location = "Operations / Meeting (192.168.23.168)"
                }
            };

            SaveConfiguration();
        }

        public void RefreshAllStatusAsync()
        {
            btnRefresh.Enabled = false;
            lblStatus.Text = "Checking network and installed printer statuses...";
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.Visible = true;

            Task.Factory.StartNew(() =>
            {
                HashSet<string> installedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                {
                    installedNames.Add(p);
                }

                Parallel.ForEach(printers, printer =>
                {
                    printer.IsInstalled = installedNames.Contains(printer.displayName) ||
                                          installedNames.Contains(printer.modelName);

                    try
                    {
                        using (Ping p = new Ping())
                        {
                            PingReply reply = p.Send(printer.ip, 900);
                            if (reply.Status == IPStatus.Success)
                            {
                                printer.IsOnline = true;
                                printer.PingMs = reply.RoundtripTime;
                            }
                            else
                            {
                                printer.IsOnline = false;
                            }
                        }
                    }
                    catch
                    {
                        printer.IsOnline = false;
                    }
                });

                this.Invoke((Action)(() =>
                {
                    RenderPrinterCards();
                    progressBar.Visible = false;
                    btnRefresh.Enabled = true;
                    lblStatus.Text = "Ready - All printer statuses updated.";
                }));
            });
        }

        private void RenderPrinterCards()
        {
            flowPrinters.SuspendLayout();
            flowPrinters.Controls.Clear();

            int cardWidth = flowPrinters.ClientSize.Width - 45;
            if (cardWidth < 850) cardWidth = 850;

            if (printers.Count == 0)
            {
                Panel emptyCard = new Panel();
                emptyCard.Size = new Size(cardWidth, 160);
                emptyCard.BackColor = Color.White;
                emptyCard.Padding = new Padding(24);
                emptyCard.Paint += (s, e) =>
                {
                    ControlPaint.DrawBorder(e.Graphics, emptyCard.ClientRectangle,
                        Color.FromArgb(226, 232, 240), ButtonBorderStyle.Solid);
                };

                Label lblEmptyTitle = new Label();
                lblEmptyTitle.Text = "📭 No Printers Currently in List for this Client";
                lblEmptyTitle.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
                lblEmptyTitle.ForeColor = Color.FromArgb(51, 65, 85);
                lblEmptyTitle.Location = new Point(24, 20);
                lblEmptyTitle.AutoSize = true;
                emptyCard.Controls.Add(lblEmptyTitle);

                Label lblEmptyDesc = new Label();
                lblEmptyDesc.Text = "All previous client printers have been cleared. You can add new printers by scanning the client's network,\npre-configuring an offline machine, or restoring the office default printers below.";
                lblEmptyDesc.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
                lblEmptyDesc.ForeColor = Color.FromArgb(100, 116, 139);
                lblEmptyDesc.Location = new Point(25, 52);
                lblEmptyDesc.AutoSize = true;
                emptyCard.Controls.Add(lblEmptyDesc);

                Button btnRestoreDefaults = CreateFlatButton("🔄 Restore Office Default Printers", Color.FromArgb(51, 65, 85), Color.White, 240, 36);
                btnRestoreDefaults.Location = new Point(26, 105);
                btnRestoreDefaults.Click += (s, e) => {
                    CreateDefaultConfiguration();
                    RefreshAllStatusAsync();
                };
                emptyCard.Controls.Add(btnRestoreDefaults);

                Button btnAddNow = CreateFlatButton("➕ Add Printer Manually (Offline)", Color.FromArgb(16, 185, 129), Color.White, 240, 36);
                btnAddNow.Location = new Point(280, 105);
                btnAddNow.Click += (s, e) => OpenManualAddDialog();
                emptyCard.Controls.Add(btnAddNow);

                flowPrinters.Controls.Add(emptyCard);
                flowPrinters.ResumeLayout();
                return;
            }

            foreach (var p in printers)
            {
                Panel card = new Panel();
                card.Size = new Size(cardWidth, 105);
                card.BackColor = Color.White;
                card.Margin = new Padding(0, 0, 0, 14);
                card.Paint += (s, e) =>
                {
                    ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle,
                        Color.FromArgb(226, 232, 240), ButtonBorderStyle.Solid);
                };

                // Left color status strip (Green if installed, Red if Ricoh default)
                Panel leftBar = new Panel();
                leftBar.Dock = DockStyle.Left;
                leftBar.Width = 6;
                leftBar.BackColor = p.IsInstalled ? Color.FromArgb(16, 185, 129) : Color.FromArgb(207, 16, 45);
                card.Controls.Add(leftBar);

                // Right action buttons container (Docked Right: never collides with text!)
                FlowLayoutPanel actionPanel = new FlowLayoutPanel();
                actionPanel.Dock = DockStyle.Right;
                actionPanel.FlowDirection = FlowDirection.RightToLeft;
                actionPanel.WrapContents = false;
                actionPanel.Width = 460;
                actionPanel.Padding = new Padding(0, 28, 16, 0);
                actionPanel.BackColor = Color.Transparent;

                PrinterConfig currentPrinter = p;

                Button btnDelete = CreateFlatButton("🗑️ Delete", Color.FromArgb(254, 226, 226), Color.FromArgb(220, 38, 38), 85, 40);
                btnDelete.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                btnDelete.Margin = new Padding(6, 0, 0, 0);
                btnDelete.Click += (s, e) => DeletePrinter(currentPrinter);

                if (!p.IsInstalled)
                {
                    Button btnInstall = CreateFlatButton("📥 1-Click Install", Color.FromArgb(207, 16, 45), Color.White, 150, 40);
                    btnInstall.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                    btnInstall.Click += (s, e) => ExecuteInstallPrinter(currentPrinter);
                    actionPanel.Controls.Add(btnInstall);
                    actionPanel.Controls.Add(btnDelete);
                }
                else
                {
                    btnDelete.Height = 40;
                    btnDelete.Width = 85;

                    Button btnTest = CreateFlatButton("🖨️ Test Page", Color.FromArgb(2, 132, 199), Color.White, 105, 40);
                    btnTest.Click += (s, e) => PrintTestPage(currentPrinter);
                    actionPanel.Controls.Add(btnTest);

                    Button btnDefault = CreateFlatButton("⭐ Set Default", Color.FromArgb(71, 85, 105), Color.White, 105, 40);
                    btnDefault.Click += (s, e) => SetAsDefault(currentPrinter);
                    actionPanel.Controls.Add(btnDefault);

                    Button btnReinstall = CreateFlatButton("🔄 Reinstall", Color.FromArgb(226, 232, 240), Color.FromArgb(30, 41, 59), 90, 40);
                    btnReinstall.Click += (s, e) => ExecuteInstallPrinter(currentPrinter);
                    actionPanel.Controls.Add(btnReinstall);

                    actionPanel.Controls.Add(btnDelete);
                }

                // Middle Info Panel (Docked Fill: uses all remaining space between leftBar and actionPanel)
                Panel infoPanel = new Panel();
                infoPanel.Dock = DockStyle.Fill;
                infoPanel.BackColor = Color.Transparent;

                Label lblName = new Label();
                lblName.Text = p.displayName;
                lblName.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
                lblName.ForeColor = Color.FromArgb(15, 23, 42);
                lblName.Location = new Point(16, 12);
                lblName.AutoSize = true;
                infoPanel.Controls.Add(lblName);

                Label lblDetails = new Label();
                lblDetails.Text = string.Format("Model: {0}  |  IP: {1}  |  Location: {2}", p.modelName, p.ip, p.location);
                lblDetails.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
                lblDetails.ForeColor = Color.FromArgb(100, 116, 139);
                lblDetails.Location = new Point(17, 40);
                lblDetails.AutoSize = true;
                infoPanel.Controls.Add(lblDetails);

                Label lblNetwork = new Label();
                if (p.IsOnline)
                {
                    lblNetwork.Text = "● Online (" + p.PingMs + "ms)";
                    lblNetwork.ForeColor = Color.FromArgb(16, 185, 129);
                }
                else
                {
                    lblNetwork.Text = "○ Offline / Asleep";
                    lblNetwork.ForeColor = Color.FromArgb(148, 163, 184);
                }
                lblNetwork.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                lblNetwork.Location = new Point(17, 68);
                lblNetwork.AutoSize = true;
                infoPanel.Controls.Add(lblNetwork);

                Label lblInstallState = new Label();
                if (p.IsInstalled)
                {
                    lblInstallState.Text = "✔ Installed on this PC";
                    lblInstallState.ForeColor = Color.FromArgb(16, 185, 129);
                }
                else
                {
                    lblInstallState.Text = "✖ Not Installed";
                    lblInstallState.ForeColor = Color.FromArgb(225, 29, 72);
                }
                lblInstallState.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                lblInstallState.Location = new Point(165, 68);
                lblInstallState.AutoSize = true;
                infoPanel.Controls.Add(lblInstallState);

                card.Controls.Add(infoPanel);
                card.Controls.Add(actionPanel);

                flowPrinters.Controls.Add(card);
            }

            flowPrinters.ResumeLayout();
        }

        private void DeletePrinter(PrinterConfig printer)
        {
            if (!printer.IsInstalled)
            {
                DialogResult dr = MessageBox.Show(
                    "Are you sure you want to remove '" + printer.displayName + "' (" + printer.ip + ") from this list?",
                    "Remove Printer",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (dr == DialogResult.Yes)
                {
                    printers.Remove(printer);
                    SaveConfiguration();
                    Log("Removed '" + printer.displayName + "' from printer list.");
                    RenderPrinterCards();
                }
            }
            else
            {
                DialogResult dr = MessageBox.Show(
                    "'" + printer.displayName + "' is currently installed on this PC.\n\n" +
                    "• Click [Yes] to remove from list AND uninstall from Windows.\n" +
                    "• Click [No] to remove from list only (keep Windows driver).\n" +
                    "• Click [Cancel] to abort.",
                    "Remove Printer",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (dr == DialogResult.Cancel) return;

                if (dr == DialogResult.Yes)
                {
                    Log("Uninstalling printer from Windows: " + printer.displayName);
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                            "-NoProfile -ExecutionPolicy Bypass -Command \"Remove-Printer -Name '" + printer.displayName.Replace("'", "''") + "' -ErrorAction SilentlyContinue\"");
                        psi.CreateNoWindow = true;
                        psi.UseShellExecute = false;
                        Process.Start(psi).WaitForExit();
                    }
                    catch (Exception ex)
                    {
                        Log("Error removing Windows printer: " + ex.Message);
                    }
                }

                printers.Remove(printer);
                SaveConfiguration();
                Log("Removed '" + printer.displayName + "' from printer list.");
                RefreshAllStatusAsync();
            }
        }

        private void ExecuteInstallPrinter(PrinterConfig printer)
        {
            string infFullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, printer.infRelativePath);
            if (!File.Exists(infFullPath))
            {
                MessageBox.Show(
                    "Driver INF file not found at:\n" + infFullPath +
                    "\n\nPlease ensure the 'drivers' folder is beside this program.",
                    "Driver Missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            bottomContainer.Height = 180;
            btnToggleLog.Text = "✖ Hide Activity Log";

            lblStatus.Text = "Installing " + printer.displayName + "... Please wait...";
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.Visible = true;
            btnRefresh.Enabled = false;
            btnScan.Enabled = false;
            btnManualAdd.Enabled = false;

            Log("==================================================");
            Log("START INSTALLATION: " + printer.displayName);
            Log("Target IP: " + printer.ip);
            Log("Driver Name: " + printer.modelName);
            Log("Driver INF: " + infFullPath);

            Task.Factory.StartNew(() =>
            {
                bool success = false;
                string errorMsg = "";

                try
                {
                    Log("[1/3] Staging driver with pnputil into Driver Store...");
                    ProcessStartInfo psiPnp = new ProcessStartInfo("pnputil.exe", "/add-driver \"" + infFullPath + "\" /install");
                    psiPnp.CreateNoWindow = true;
                    psiPnp.UseShellExecute = false;
                    psiPnp.RedirectStandardOutput = true;
                    psiPnp.RedirectStandardError = true;

                    using (Process pPnp = Process.Start(psiPnp))
                    {
                        string pnpOut = pPnp.StandardOutput.ReadToEnd();
                        pPnp.WaitForExit();
                        Log("pnputil: " + pnpOut.Trim().Replace("\r", " ").Replace("\n", " "));
                    }

                    Log("[2/3] Configuring TCP/IP port and Windows print queue...");
                    string psScript = string.Format(
                        "$ErrorActionPreference = 'Stop'; " +
                        "$portName = 'IP_{0}'; " +
                        "if (-not (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue)) {{ Add-PrinterPort -Name $portName -PrinterHostAddress '{0}' }}; " +
                        "if (-not (Get-PrinterDriver -Name '{1}' -ErrorAction SilentlyContinue)) {{ Add-PrinterDriver -Name '{1}' }}; " +
                        "if (-not (Get-Printer -Name '{2}' -ErrorAction SilentlyContinue)) {{ Add-Printer -Name '{2}' -DriverName '{1}' -PortName $portName }} else {{ Set-Printer -Name '{2}' -DriverName '{1}' -PortName $portName }};",
                        printer.ip,
                        printer.modelName.Replace("'", "''"),
                        printer.displayName.Replace("'", "''")
                    );

                    ProcessStartInfo psiPs = new ProcessStartInfo(
                        "powershell.exe",
                        "-NoProfile -ExecutionPolicy Bypass -Command \"" + psScript + "\""
                    );
                    psiPs.CreateNoWindow = true;
                    psiPs.UseShellExecute = false;
                    psiPs.RedirectStandardOutput = true;
                    psiPs.RedirectStandardError = true;

                    using (Process pPs = Process.Start(psiPs))
                    {
                        string stdOut = pPs.StandardOutput.ReadToEnd();
                        string stdErr = pPs.StandardError.ReadToEnd();
                        pPs.WaitForExit();

                        if (!string.IsNullOrEmpty(stdOut)) Log("PowerShell: " + stdOut.Trim());
                        if (!string.IsNullOrEmpty(stdErr)) Log("PowerShell Warning/Error: " + stdErr.Trim());

                        if (pPs.ExitCode == 0)
                        {
                            success = true;
                        }
                        else
                        {
                            errorMsg = stdErr;
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorMsg = ex.Message;
                }

                this.Invoke((Action)(() =>
                {
                    progressBar.Visible = false;
                    btnRefresh.Enabled = true;
                    btnScan.Enabled = true;
                    btnManualAdd.Enabled = true;

                    if (success)
                    {
                        Log("[3/3] SUCCESS: " + printer.displayName + " is installed and ready!");
                        lblStatus.Text = printer.displayName + " installed successfully!";
                        RefreshAllStatusAsync();

                        DialogResult dr = MessageBox.Show(
                            printer.displayName + " has been successfully configured on this PC!\n(Queue pointing to IP " + printer.ip + ")\n\nWould you like to print a test page now?",
                            "Configuration Successful",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information);

                        if (dr == DialogResult.Yes)
                        {
                            PrintTestPage(printer);
                        }
                    }
                    else
                    {
                        Log("ERROR during installation: " + errorMsg);
                        lblStatus.Text = "Installation failed. Check log.";
                        MessageBox.Show(
                            "An error occurred while installing the printer:\n\n" + errorMsg +
                            "\n\nPlease ensure this application was run as Administrator.",
                            "Installation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }));
            });
        }

        private void PrintTestPage(PrinterConfig printer)
        {
            try
            {
                Log("Sending Windows test page to: " + printer.displayName);
                ProcessStartInfo psi = new ProcessStartInfo("rundll32.exe", "printui.dll,PrintUIEntry /k /n \"" + printer.displayName + "\"");
                Process.Start(psi);
                lblStatus.Text = "Test page sent to " + printer.displayName;
            }
            catch (Exception ex)
            {
                Log("Error printing test page: " + ex.Message);
            }
        }

        private void SetAsDefault(PrinterConfig printer)
        {
            try
            {
                string cmd = string.Format("(New-Object -ComObject WScript.Network).SetDefaultPrinter('{0}')", printer.displayName.Replace("'", "''"));
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"" + cmd + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process.Start(psi).WaitForExit();

                Log("Set default printer to: " + printer.displayName);
                lblStatus.Text = "Default printer set to " + printer.displayName;
                MessageBox.Show(printer.displayName + " is now your default printer.", "Default Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log("Error setting default printer: " + ex.Message);
            }
        }

        private void OpenManualAddDialog()
        {
            using (ManualAddForm mf = new ManualAddForm())
            {
                if (mf.ShowDialog(this) == DialogResult.OK && mf.CreatedPrinter != null)
                {
                    printers.Add(mf.CreatedPrinter);
                    SaveConfiguration();
                    Log("Added manual printer: " + mf.CreatedPrinter.displayName + " (" + mf.CreatedPrinter.ip + ")");
                    RenderPrinterCards();

                    if (mf.InstallImmediately)
                    {
                        ExecuteInstallPrinter(mf.CreatedPrinter);
                    }
                }
            }
        }

        private void OpenScannerDialog()
        {
            using (ScannerForm sf = new ScannerForm(printers, configPath))
            {
                if (sf.ShowDialog(this) == DialogResult.OK)
                {
                    LoadConfiguration();
                    RefreshAllStatusAsync();
                }
            }
        }

        private void OpenAddressBookDialog()
        {
            using (AddressBookForm abf = new AddressBookForm(printers))
            {
                abf.ShowDialog(this);
            }
        }

        public void Log(string message)
        {
            string line = string.Format("[{0:HH:mm:ss}] {1}\r\n", DateTime.Now, message);
            if (this.InvokeRequired)
            {
                this.Invoke((Action)(() => txtLog.AppendText(line)));
            }
            else
            {
                txtLog.AppendText(line);
            }
        }
    }

    public class ManualAddForm : Form
    {
        private ComboBox cmbModel;
        private TextBox txtIp;
        private TextBox txtDisplayName;
        private TextBox txtLocation;
        private Button btnInstallNow;
        private Button btnAddToList;
        private List<ModelProfile> profiles;

        public PrinterConfig CreatedPrinter { get; private set; }
        public bool InstallImmediately { get; private set; }

        public ManualAddForm()
        {
            profiles = ModelProfile.LoadFromDriversFolder(AppDomain.CurrentDomain.BaseDirectory);
            InitializeManualAddUI();
        }

        private void InitializeManualAddUI()
        {
            this.Text = "Add Printer Manually (Offline Pre-Configuration)";
            this.Size = new Size(640, 430);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            Label lblHeading = new Label();
            lblHeading.Text = "Pre-Configure Printer & Driver (No Network Required)";
            lblHeading.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            lblHeading.ForeColor = Color.FromArgb(15, 23, 42);
            lblHeading.Location = new Point(24, 18);
            lblHeading.AutoSize = true;
            this.Controls.Add(lblHeading);

            Label lblSub = new Label();
            lblSub.Text = "Discovered " + profiles.Count + " Ricoh models from your 'drivers' folder.\nSet the planned IP and install the driver now, so when the network is connected\nlater, the machine prints immediately without reinstalling.";
            lblSub.ForeColor = Color.FromArgb(100, 116, 139);
            lblSub.Location = new Point(25, 46);
            lblSub.AutoSize = true;
            this.Controls.Add(lblSub);

            // Model Selection
            Label lblModel = new Label();
            lblModel.Text = "Ricoh Model:";
            lblModel.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblModel.Location = new Point(25, 115);
            lblModel.AutoSize = true;
            this.Controls.Add(lblModel);

            cmbModel = new ComboBox();
            cmbModel.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var p in profiles) cmbModel.Items.Add(p);
            cmbModel.SelectedIndex = 0;
            cmbModel.Location = new Point(160, 112);
            cmbModel.Size = new Size(430, 28);
            cmbModel.SelectedIndexChanged += (s, e) => AutoUpdateDisplayName();
            this.Controls.Add(cmbModel);

            // IP Address
            Label lblIp = new Label();
            lblIp.Text = "Target IP Address:";
            lblIp.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblIp.Location = new Point(25, 160);
            lblIp.AutoSize = true;
            this.Controls.Add(lblIp);

            txtIp = new TextBox();
            txtIp.Text = "192.168.1.200";
            txtIp.Location = new Point(160, 157);
            txtIp.Size = new Size(200, 26);
            txtIp.TextChanged += (s, e) => AutoUpdateDisplayName();
            this.Controls.Add(txtIp);

            // Display Name
            Label lblName = new Label();
            lblName.Text = "Printer Name:";
            lblName.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblName.Location = new Point(25, 205);
            lblName.AutoSize = true;
            this.Controls.Add(lblName);

            txtDisplayName = new TextBox();
            txtDisplayName.Text = "Ricoh MFP (200)";
            txtDisplayName.Location = new Point(160, 202);
            txtDisplayName.Size = new Size(430, 26);
            this.Controls.Add(txtDisplayName);

            // Location
            Label lblLoc = new Label();
            lblLoc.Text = "Location / Client:";
            lblLoc.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblLoc.Location = new Point(25, 250);
            lblLoc.AutoSize = true;
            this.Controls.Add(lblLoc);

            txtLocation = new TextBox();
            txtLocation.Text = "Main Office";
            txtLocation.Location = new Point(160, 247);
            txtLocation.Size = new Size(430, 26);
            this.Controls.Add(txtLocation);

            // Buttons
            btnInstallNow = new Button();
            btnInstallNow.Text = "📥 Install Driver & Queue Now";
            btnInstallNow.Location = new Point(160, 315);
            btnInstallNow.Size = new Size(230, 40);
            btnInstallNow.BackColor = Color.FromArgb(207, 16, 45);
            btnInstallNow.ForeColor = Color.White;
            btnInstallNow.FlatStyle = FlatStyle.Flat;
            btnInstallNow.FlatAppearance.BorderSize = 0;
            btnInstallNow.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnInstallNow.Cursor = Cursors.Hand;
            btnInstallNow.Click += (s, e) => SaveAndClose(true);
            this.Controls.Add(btnInstallNow);

            btnAddToList = new Button();
            btnAddToList.Text = "+ Add to List Only";
            btnAddToList.Location = new Point(400, 315);
            btnAddToList.Size = new Size(190, 40);
            btnAddToList.BackColor = Color.FromArgb(51, 65, 85);
            btnAddToList.ForeColor = Color.White;
            btnAddToList.FlatStyle = FlatStyle.Flat;
            btnAddToList.FlatAppearance.BorderSize = 0;
            btnAddToList.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnAddToList.Cursor = Cursors.Hand;
            btnAddToList.Click += (s, e) => SaveAndClose(false);
            this.Controls.Add(btnAddToList);

            AutoUpdateDisplayName();
        }

        private void AutoUpdateDisplayName()
        {
            if (cmbModel.SelectedItem == null) return;
            ModelProfile p = (ModelProfile)cmbModel.SelectedItem;
            string ip = txtIp.Text.Trim();
            string last = ip.Contains(".") ? ip.Substring(ip.LastIndexOf('.') + 1) : ip;
            txtDisplayName.Text = p.Prefix + " (" + last + ")";
        }

        private void SaveAndClose(bool installNow)
        {
            string ip = txtIp.Text.Trim();
            if (string.IsNullOrEmpty(ip) || !ip.Contains("."))
            {
                MessageBox.Show("Please enter a valid IP address.", "Invalid IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ModelProfile prof = (ModelProfile)cmbModel.SelectedItem;
            string dName = txtDisplayName.Text.Trim();
            if (string.IsNullOrEmpty(dName)) dName = prof.Prefix + " (" + ip + ")";

            string loc = txtLocation.Text.Trim();
            if (string.IsNullOrEmpty(loc)) loc = "Client Office (" + ip + ")";

            CreatedPrinter = new PrinterConfig
            {
                id = "manual_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                displayName = dName,
                modelName = prof.DriverName,
                ip = ip,
                infRelativePath = prof.InfPath,
                location = loc
            };

            InstallImmediately = installNow;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }

    public class ScannerForm : Form
    {
        private List<PrinterConfig> printers;
        private string configPath;
        private ListView lvResults;
        private ProgressBar pbScan;
        private Label lblScanStatus;
        private Button btnStartScan;
        private Button btnAddSelected;
        private ComboBox cmbModel;
        private TextBox txtSubnet;
        private List<ModelProfile> profiles;

        public ScannerForm(List<PrinterConfig> existingPrinters, string cfgPath)
        {
            this.printers = existingPrinters;
            this.configPath = cfgPath;
            this.profiles = ModelProfile.LoadFromDriversFolder(AppDomain.CurrentDomain.BaseDirectory);
            InitializeScannerUI();
        }

        private void InitializeScannerUI()
        {
            this.Text = "Network Printer Auto-Discovery";
            this.Size = new Size(860, 550);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            Label lblInfo = new Label();
            lblInfo.Text = "Scans your office network to automatically detect Ricoh printer models (via Port 9100 / Web Image Monitor).\nLoaded " + profiles.Count + " drivers dynamically from your 'drivers' folder.";
            lblInfo.Location = new Point(20, 12);
            lblInfo.AutoSize = true;
            this.Controls.Add(lblInfo);

            Label lblSub = new Label();
            lblSub.Text = "Subnet:";
            lblSub.Location = new Point(20, 52);
            lblSub.AutoSize = true;
            this.Controls.Add(lblSub);

            txtSubnet = new TextBox();
            txtSubnet.Text = "192.168.23";
            txtSubnet.Location = new Point(80, 49);
            txtSubnet.Size = new Size(110, 25);
            this.Controls.Add(txtSubnet);

            btnStartScan = new Button();
            btnStartScan.Text = "🔍 Start Fast Scan (1 - 254)";
            btnStartScan.Location = new Point(205, 47);
            btnStartScan.Size = new Size(190, 29);
            btnStartScan.BackColor = Color.FromArgb(207, 16, 45);
            btnStartScan.ForeColor = Color.White;
            btnStartScan.FlatStyle = FlatStyle.Flat;
            btnStartScan.FlatAppearance.BorderSize = 0;
            btnStartScan.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnStartScan.Click += (s, e) => StartSubnetScan();
            this.Controls.Add(btnStartScan);

            pbScan = new ProgressBar();
            pbScan.Location = new Point(410, 49);
            pbScan.Size = new Size(410, 25);
            pbScan.Maximum = 254;
            this.Controls.Add(pbScan);

            lblScanStatus = new Label();
            lblScanStatus.Text = "Ready to scan.";
            lblScanStatus.Location = new Point(20, 84);
            lblScanStatus.AutoSize = true;
            lblScanStatus.ForeColor = Color.FromArgb(100, 116, 139);
            this.Controls.Add(lblScanStatus);

            lvResults = new ListView();
            lvResults.View = View.Details;
            lvResults.FullRowSelect = true;
            lvResults.GridLines = true;
            lvResults.Location = new Point(20, 112);
            lvResults.Size = new Size(800, 290);
            lvResults.Columns.Add("IP Address", 130);
            lvResults.Columns.Add("Detected Ricoh Model", 210);
            lvResults.Columns.Add("Port 9100", 85);
            lvResults.Columns.Add("Current Office Status", 340);
            lvResults.SelectedIndexChanged += (s, e) => OnSelectionChanged();
            this.Controls.Add(lvResults);

            Label lblAssign = new Label();
            lblAssign.Text = "Assign Model:";
            lblAssign.Location = new Point(20, 432);
            lblAssign.AutoSize = true;
            this.Controls.Add(lblAssign);

            cmbModel = new ComboBox();
            cmbModel.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var prof in profiles)
            {
                cmbModel.Items.Add(prof);
            }
            if (cmbModel.Items.Count > 0) cmbModel.SelectedIndex = 0;
            cmbModel.Location = new Point(125, 428);
            cmbModel.Size = new Size(440, 28);
            this.Controls.Add(cmbModel);

            btnAddSelected = new Button();
            btnAddSelected.Text = "+ Add to Main List";
            btnAddSelected.Location = new Point(585, 427);
            btnAddSelected.Size = new Size(235, 32);
            btnAddSelected.BackColor = Color.FromArgb(16, 185, 129);
            btnAddSelected.ForeColor = Color.White;
            btnAddSelected.FlatStyle = FlatStyle.Flat;
            btnAddSelected.FlatAppearance.BorderSize = 0;
            btnAddSelected.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnAddSelected.Click += (s, e) => AddSelectedPrinter();
            this.Controls.Add(btnAddSelected);
        }

        private void OnSelectionChanged()
        {
            if (lvResults.SelectedItems.Count == 0) return;
            string detectedModel = lvResults.SelectedItems[0].SubItems[1].Text;

            // Normalize detected model
            string cleanDetected = detectedModel.Replace("RICOH", "").Trim();

            for (int i = 0; i < profiles.Count; i++)
            {
                if (!string.IsNullOrEmpty(cleanDetected) &&
                    (profiles[i].DriverName.IndexOf(cleanDetected, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     cleanDetected.IndexOf(profiles[i].Prefix, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    cmbModel.SelectedIndex = i;
                    return;
                }
            }
        }

        private void StartSubnetScan()
        {
            string subnet = txtSubnet.Text.Trim().TrimEnd('.');
            btnStartScan.Enabled = false;
            btnAddSelected.Enabled = false;
            lvResults.Items.Clear();
            pbScan.Value = 0;
            lblScanStatus.Text = "Scanning " + subnet + ".1 to " + subnet + ".254 on Port 9100...";

            Task.Factory.StartNew(() =>
            {
                int completed = 0;
                List<string> foundIps = new List<string>();

                Parallel.For(1, 255, new ParallelOptions { MaxDegreeOfParallelism = 40 }, i =>
                {
                    string targetIp = subnet + "." + i;
                    bool isOpen = false;

                    try
                    {
                        using (TcpClient client = new TcpClient())
                        {
                            IAsyncResult ar = client.BeginConnect(targetIp, 9100, null, null);
                            if (ar.AsyncWaitHandle.WaitOne(350, false))
                            {
                                if (client.Connected)
                                {
                                    client.EndConnect(ar);
                                    isOpen = true;
                                }
                            }
                        }
                    }
                    catch {}

                    System.Threading.Interlocked.Increment(ref completed);

                    if (isOpen)
                    {
                        lock (foundIps) { foundIps.Add(targetIp); }

                        string detectedModel = DetectRicohModel(targetIp);

                        this.Invoke((Action)(() =>
                        {
                            string match = "Unlisted / Ready to Add";
                            foreach (var p in printers)
                            {
                                if (p.ip == targetIp)
                                {
                                    match = "In Main List (" + p.displayName + ")";
                                    break;
                                }
                            }

                            ListViewItem lvi = new ListViewItem(targetIp);
                            lvi.SubItems.Add(detectedModel);
                            lvi.SubItems.Add("OPEN");
                            lvi.SubItems.Add(match);
                            lvi.ForeColor = Color.FromArgb(16, 185, 129);
                            lvi.Tag = detectedModel;
                            lvResults.Items.Add(lvi);
                        }));
                    }

                    if (completed % 10 == 0 || completed == 254)
                    {
                        this.Invoke((Action)(() =>
                        {
                            pbScan.Value = Math.Min(completed, 254);
                        }));
                    }
                });

                this.Invoke((Action)(() =>
                {
                    pbScan.Value = 254;
                    btnStartScan.Enabled = true;
                    btnAddSelected.Enabled = true;
                    lblScanStatus.Text = "Scan Complete! Found " + foundIps.Count + " active printer device(s).";
                }));
            });
        }

        private string DetectRicohModel(string ip)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult ar = client.BeginConnect(ip, 9100, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(800, false) && client.Connected)
                    {
                        client.EndConnect(ar);
                        using (NetworkStream ns = client.GetStream())
                        {
                            ns.ReadTimeout = 1200;
                            ns.WriteTimeout = 800;
                            byte[] cmd = Encoding.ASCII.GetBytes("@PJL INFO ID\r\n");
                            ns.Write(cmd, 0, cmd.Length);

                            byte[] buf = new byte[1024];
                            int read = ns.Read(buf, 0, buf.Length);
                            string resp = Encoding.ASCII.GetString(buf, 0, read);
                            Match m = Regex.Match(resp, "\"([^\"]+)\"");
                            if (m.Success)
                            {
                                return m.Groups[1].Value.Trim();
                            }
                        }
                    }
                }
            }
            catch {}

            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://" + ip + "/web/guest/en/websys/webArch/header.cgi");
                req.Timeout = 1500;
                req.ReadWriteTimeout = 1500;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string html = sr.ReadToEnd();
                    Match m = Regex.Match(html, "<h2\\s+id=[\"']modelName[\"'][^>]*>([^<]+)</h2>", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        string name = m.Groups[1].Value.Trim();
                        return name.StartsWith("RICOH", StringComparison.OrdinalIgnoreCase) ? name : "RICOH " + name;
                    }

                    Match mGen = Regex.Match(html, "(MP\\s*C\\d+|IM\\s*C\\d+|Aficio\\s*MP\\s*C\\d+)", RegexOptions.IgnoreCase);
                    if (mGen.Success)
                    {
                        return "RICOH " + mGen.Groups[1].Value.Trim();
                    }
                }
            }
            catch {}

            return "Ricoh MFP (Port 9100)";
        }

        private void AddSelectedPrinter()
        {
            if (lvResults.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select an IP address from the list above.", "Select Device", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string ip = lvResults.SelectedItems[0].Text;
            ModelProfile prof = (ModelProfile)cmbModel.SelectedItem;

            foreach (var p in printers)
            {
                if (p.ip == ip)
                {
                    MessageBox.Show("Printer with IP " + ip + " is already in the main list (" + p.displayName + ").", "Already Exists", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            string lastOctet = ip.Substring(ip.LastIndexOf('.') + 1);
            PrinterConfig newPrinter = new PrinterConfig
            {
                id = "custom_" + ip.Replace(".", "_"),
                displayName = prof.Prefix + " (" + lastOctet + ")",
                modelName = prof.DriverName,
                ip = ip,
                infRelativePath = prof.InfPath,
                location = "Office Network (" + ip + ")"
            };

            printers.Add(newPrinter);
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                File.WriteAllText(configPath, js.Serialize(printers));
            }
            catch {}

            MessageBox.Show("Added " + newPrinter.displayName + " (" + ip + ") to the main printer list!", "Added", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }

    // =========================================================================
    // RICOH SCANNER ADDRESS BOOK MANAGER (SOAP DIRECT NETWORK + CSV / PROFILES)
    // =========================================================================

    public class RicohAddressEntry
    {
        public int EntryIndex { get; set; }
        public string DisplayKey { get; set; }
        public string Name { get; set; }
        public string TitleIndex { get; set; }

        public bool HasEmail { get; set; }
        public string Email { get; set; }

        public bool HasFolder { get; set; }
        public string FolderProtocol { get; set; }
        public string FolderPath { get; set; }
        public string FolderUser { get; set; }
        public string FolderPassword { get; set; }

        public RicohAddressEntry()
        {
            EntryIndex = 1;
            DisplayKey = "";
            Name = "";
            TitleIndex = "";
            HasEmail = false;
            Email = "";
            HasFolder = false;
            FolderProtocol = "SMB";
            FolderPath = "";
            FolderUser = "";
            FolderPassword = "";
        }

        public RicohAddressEntry Clone()
        {
            RicohAddressEntry c = new RicohAddressEntry();
            c.EntryIndex = this.EntryIndex;
            c.DisplayKey = this.DisplayKey;
            c.Name = this.Name;
            c.TitleIndex = this.TitleIndex;
            c.HasEmail = this.HasEmail;
            c.Email = this.Email;
            c.HasFolder = this.HasFolder;
            c.FolderProtocol = this.FolderProtocol;
            c.FolderPath = this.FolderPath;
            c.FolderUser = this.FolderUser;
            c.FolderPassword = this.FolderPassword;
            return c;
        }
    }

    public static class RicohSoapClient
    {
        private static string EscapeXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        public static string PostSoap(string ip, string action, string xmlBody, int timeoutMs)
        {
            string url = "http://" + ip + "/DH/udirectory";
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "text/xml; charset=utf-8";
            req.Headers.Add("SOAPAction", "\"http://www.ricoh.co.jp/xmlns/soap/rdh/udirectory#" + action + "\"");
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;

            byte[] bytes = Encoding.UTF8.GetBytes(xmlBody);
            req.ContentLength = bytes.Length;
            using (Stream os = req.GetRequestStream())
            {
                os.Write(bytes, 0, bytes.Length);
            }

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        public static string StartSession(string ip, string adminUser, string adminPass)
        {
            string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">\r\n" +
                "  <s:Body>\r\n" +
                "    <u:startSession xmlns:u=\"http://www.ricoh.co.jp/xmlns/soap/rdh/udirectory\">\r\n" +
                "      <authInfo>\r\n" +
                "        <authType>administrator</authType>\r\n" +
                "        <loginName>" + EscapeXml(adminUser) + "</loginName>\r\n" +
                "        <password>" + EscapeXml(adminPass) + "</password>\r\n" +
                "      </authInfo>\r\n" +
                "    </u:startSession>\r\n" +
                "  </s:Body>\r\n" +
                "</s:Envelope>";

            string resp = PostSoap(ip, "startSession", xml, 7000);
            Match m = Regex.Match(resp, "<sessionId>([^<]+)</sessionId>", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                return m.Groups[1].Value.Trim();
            }
            throw new Exception("Printer did not return a valid session ID. Response: " + resp);
        }

        public static void TerminateSession(string ip, string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            try
            {
                string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">\r\n" +
                    "  <s:Body>\r\n" +
                    "    <u:terminateSession xmlns:u=\"http://www.ricoh.co.jp/xmlns/soap/rdh/udirectory\">\r\n" +
                    "      <sessionId>" + EscapeXml(sessionId) + "</sessionId>\r\n" +
                    "    </u:terminateSession>\r\n" +
                    "  </s:Body>\r\n" +
                    "</s:Envelope>";
                PostSoap(ip, "terminateSession", xml, 3000);
            }
            catch {}
        }

        public static List<RicohAddressEntry> ReadAddressBook(string ip, string adminUser, string adminPass)
        {
            string sessionId = null;
            List<RicohAddressEntry> list = new List<RicohAddressEntry>();
            try
            {
                sessionId = StartSession(ip, adminUser, adminPass);
                string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">\r\n" +
                    "  <s:Body>\r\n" +
                    "    <u:searchObjects xmlns:u=\"http://www.ricoh.co.jp/xmlns/soap/rdh/udirectory\">\r\n" +
                    "      <sessionId>" + EscapeXml(sessionId) + "</sessionId>\r\n" +
                    "      <query></query>\r\n" +
                    "    </u:searchObjects>\r\n" +
                    "  </s:Body>\r\n" +
                    "</s:Envelope>";

                string resp = PostSoap(ip, "searchObjects", xml, 10000);

                XmlDocument doc = new XmlDocument();
                doc.LoadXml(resp);

                XmlNodeList items = doc.SelectNodes("//*[local-name()='item']");
                if (items == null || items.Count == 0)
                {
                    items = doc.SelectNodes("//*[local-name()='directoryObject']");
                }

                int defaultIndex = 1;
                if (items != null)
                {
                    foreach (XmlNode node in items)
                    {
                        try
                        {
                            RicohAddressEntry entry = new RicohAddressEntry();
                            XmlNode idxNode = node.SelectSingleNode(".//*[local-name()='index' or local-name()='entryIndex']");
                            if (idxNode != null)
                            {
                                int val;
                                if (int.TryParse(idxNode.InnerText, out val)) entry.EntryIndex = val;
                                else entry.EntryIndex = defaultIndex;
                            }
                            else
                            {
                                entry.EntryIndex = defaultIndex;
                            }

                            XmlNode nameNode = node.SelectSingleNode(".//*[local-name()='name']");
                            if (nameNode != null) entry.Name = nameNode.InnerText;

                            XmlNode dispNode = node.SelectSingleNode(".//*[local-name()='keyDisplay' or local-name()='displayKey']");
                            if (dispNode != null) entry.DisplayKey = dispNode.InnerText;
                            if (string.IsNullOrEmpty(entry.DisplayKey)) entry.DisplayKey = entry.Name;

                            XmlNode titleNode = node.SelectSingleNode(".//*[local-name()='title1' or local-name()='titleIndex']");
                            if (titleNode != null) entry.TitleIndex = titleNode.InnerText;

                            XmlNode mailNode = node.SelectSingleNode(".//*[local-name()='mailAddress' or local-name()='email']");
                            if (mailNode != null && !string.IsNullOrEmpty(mailNode.InnerText))
                            {
                                entry.HasEmail = true;
                                entry.Email = mailNode.InnerText;
                            }

                            XmlNode protoNode = node.SelectSingleNode(".//*[local-name()='folderProtocol']");
                            if (protoNode != null && !string.IsNullOrEmpty(protoNode.InnerText)) entry.FolderProtocol = protoNode.InnerText;

                            XmlNode pathNode = node.SelectSingleNode(".//*[local-name()='folderPath']");
                            if (pathNode != null && !string.IsNullOrEmpty(pathNode.InnerText))
                            {
                                entry.HasFolder = true;
                                entry.FolderPath = pathNode.InnerText;
                            }

                            XmlNode userNode = node.SelectSingleNode(".//*[local-name()='folderUser' or local-name()='folderUserName']");
                            if (userNode != null) entry.FolderUser = userNode.InnerText;

                            if (!string.IsNullOrEmpty(entry.Name) || !string.IsNullOrEmpty(entry.Email) || !string.IsNullOrEmpty(entry.FolderPath))
                            {
                                list.Add(entry);
                                defaultIndex++;
                            }
                        }
                        catch {}
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(sessionId))
                {
                    TerminateSession(ip, sessionId);
                }
            }

            return list;
        }

        public static void PushAddressBook(string ip, string adminUser, string adminPass, List<RicohAddressEntry> entries)
        {
            string sessionId = null;
            try
            {
                sessionId = StartSession(ip, adminUser, adminPass);

                StringBuilder sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n");
                sb.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:u=\"http://www.ricoh.co.jp/xmlns/soap/rdh/udirectory\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\r\n");
                sb.Append("  <s:Body>\r\n");
                sb.Append("    <u:putObjects>\r\n");
                sb.Append("      <sessionId>" + EscapeXml(sessionId) + "</sessionId>\r\n");
                sb.Append("      <objectList>\r\n");

                for (int i = 0; i < entries.Count; i++)
                {
                    RicohAddressEntry e = entries[i];
                    int index = e.EntryIndex > 0 ? e.EntryIndex : (i + 1);
                    string disp = string.IsNullOrEmpty(e.DisplayKey) ? e.Name : e.DisplayKey;
                    string title = string.IsNullOrEmpty(e.TitleIndex) ? (disp.Length > 0 ? disp.Substring(0, 1).ToUpper() : "A") : e.TitleIndex;

                    sb.Append("        <item>\r\n");
                    sb.Append("          <index>" + index + "</index>\r\n");
                    sb.Append("          <name>" + EscapeXml(e.Name) + "</name>\r\n");
                    sb.Append("          <properties>\r\n");

                    sb.Append("            <item><name>entryType</name><value xsi:type=\"xsd:string\">user</value></item>\r\n");
                    sb.Append("            <item><name>keyDisplay</name><value xsi:type=\"xsd:string\">" + EscapeXml(disp) + "</value></item>\r\n");
                    sb.Append("            <item><name>title1</name><value xsi:type=\"xsd:string\">" + EscapeXml(title) + "</value></item>\r\n");

                    if (e.HasEmail && !string.IsNullOrEmpty(e.Email))
                    {
                        sb.Append("            <item><name>mailAddress</name><value xsi:type=\"xsd:string\">" + EscapeXml(e.Email) + "</value></item>\r\n");
                        sb.Append("            <item><name>email</name><value xsi:type=\"xsd:string\">" + EscapeXml(e.Email) + "</value></item>\r\n");
                    }

                    if (e.HasFolder && !string.IsNullOrEmpty(e.FolderPath))
                    {
                        string proto = string.IsNullOrEmpty(e.FolderProtocol) ? "SMB" : e.FolderProtocol;
                        sb.Append("            <item><name>folderProtocol</name><value xsi:type=\"xsd:string\">" + EscapeXml(proto) + "</value></item>\r\n");
                        sb.Append("            <item><name>folderPath</name><value xsi:type=\"xsd:string\">" + EscapeXml(e.FolderPath) + "</value></item>\r\n");
                        if (!string.IsNullOrEmpty(e.FolderUser))
                        {
                            sb.Append("            <item><name>folderUser</name><value xsi:type=\"xsd:string\">" + EscapeXml(e.FolderUser) + "</value></item>\r\n");
                        }
                        if (!string.IsNullOrEmpty(e.FolderPassword))
                        {
                            sb.Append("            <item><name>folderPassword</name><value xsi:type=\"xsd:string\">" + EscapeXml(e.FolderPassword) + "</value></item>\r\n");
                        }
                    }

                    sb.Append("          </properties>\r\n");
                    sb.Append("        </item>\r\n");
                }

                sb.Append("      </objectList>\r\n");
                sb.Append("    </u:putObjects>\r\n");
                sb.Append("  </s:Body>\r\n");
                sb.Append("</s:Envelope>");

                PostSoap(ip, "putObjects", sb.ToString(), 12000);
            }
            finally
            {
                if (!string.IsNullOrEmpty(sessionId))
                {
                    TerminateSession(ip, sessionId);
                }
            }
        }
    }

    public static class RicohCsvHelper
    {
        public static void ExportRicohCsv(string path, List<RicohAddressEntry> entries)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Format Version: 5.1.1.0");
            sb.AppendLine("# Generated by Ricoh Office Printer Setup Tool");
            sb.AppendLine("# Function Name: User Data Preference");
            sb.AppendLine("Registration No.,Name,Key Display,Title 1,E-mail Address,Folder Protocol,Folder Path,Folder User,Folder Password");

            for (int i = 0; i < entries.Count; i++)
            {
                RicohAddressEntry e = entries[i];
                int no = e.EntryIndex > 0 ? e.EntryIndex : (i + 1);
                string disp = string.IsNullOrEmpty(e.DisplayKey) ? e.Name : e.DisplayKey;
                string title = string.IsNullOrEmpty(e.TitleIndex) ? (disp.Length > 0 ? disp.Substring(0, 1).ToUpper() : "A") : e.TitleIndex;
                string email = e.HasEmail ? e.Email : "";
                string proto = e.HasFolder ? (string.IsNullOrEmpty(e.FolderProtocol) ? "SMB" : e.FolderProtocol) : "";
                string folder = e.HasFolder ? e.FolderPath : "";
                string user = e.HasFolder ? e.FolderUser : "";
                string pass = e.HasFolder ? e.FolderPassword : "";

                sb.Append(no.ToString());
                sb.Append("," + EscapeCsv(e.Name));
                sb.Append("," + EscapeCsv(disp));
                sb.Append("," + EscapeCsv(title));
                sb.Append("," + EscapeCsv(email));
                sb.Append("," + EscapeCsv(proto));
                sb.Append("," + EscapeCsv(folder));
                sb.Append("," + EscapeCsv(user));
                sb.Append("," + EscapeCsv(pass));
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }

        public static List<RicohAddressEntry> ImportRicohCsv(string path)
        {
            List<RicohAddressEntry> list = new List<RicohAddressEntry>();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);

            int colReg = 0, colName = 1, colKey = 2, colTitle = 3, colMail = 4, colProto = 5, colPath = 6, colUser = 7, colPass = 8;
            bool headerFound = false;

            for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
            {
                string line = lines[lineIdx].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                List<string> cols = ParseCsvLine(line);
                if (cols.Count == 0) continue;

                if (!headerFound)
                {
                    for (int c = 0; c < cols.Count; c++)
                    {
                        string h = cols[c].ToLowerInvariant();
                        if (h.Contains("reg") || h.Contains("no")) colReg = c;
                        else if (h.Contains("name") && !h.Contains("user")) colName = c;
                        else if (h.Contains("key") || h.Contains("display")) colKey = c;
                        else if (h.Contains("title")) colTitle = c;
                        else if (h.Contains("mail") || h.Contains("email")) colMail = c;
                        else if (h.Contains("protocol")) colProto = c;
                        else if (h.Contains("path") || h.Contains("folder")) colPath = c;
                        else if (h.Contains("user") || h.Contains("login")) colUser = c;
                        else if (h.Contains("pass")) colPass = c;
                    }
                    headerFound = true;
                    continue;
                }

                RicohAddressEntry entry = new RicohAddressEntry();
                int reg;
                if (colReg < cols.Count && int.TryParse(cols[colReg], out reg)) entry.EntryIndex = reg;
                else entry.EntryIndex = list.Count + 1;

                if (colName < cols.Count) entry.Name = cols[colName];
                if (colKey < cols.Count) entry.DisplayKey = cols[colKey];
                if (string.IsNullOrEmpty(entry.DisplayKey)) entry.DisplayKey = entry.Name;
                if (colTitle < cols.Count) entry.TitleIndex = cols[colTitle];

                if (colMail < cols.Count && !string.IsNullOrEmpty(cols[colMail]))
                {
                    entry.HasEmail = true;
                    entry.Email = cols[colMail];
                }

                if (colPath < cols.Count && !string.IsNullOrEmpty(cols[colPath]))
                {
                    entry.HasFolder = true;
                    entry.FolderPath = cols[colPath];
                    entry.FolderProtocol = (colProto < cols.Count && !string.IsNullOrEmpty(cols[colProto])) ? cols[colProto] : "SMB";
                    if (colUser < cols.Count) entry.FolderUser = cols[colUser];
                    if (colPass < cols.Count) entry.FolderPassword = cols[colPass];
                }

                if (!string.IsNullOrEmpty(entry.Name) || !string.IsNullOrEmpty(entry.Email) || !string.IsNullOrEmpty(entry.FolderPath))
                {
                    list.Add(entry);
                }
            }

            return list;
        }

        private static List<string> ParseCsvLine(string line)
        {
            List<string> result = new List<string>();
            bool inQuotes = false;
            StringBuilder cur = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                    {
                        cur.Append('\"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(cur.ToString().Trim());
                    cur.Length = 0;
                }
                else
                {
                    cur.Append(c);
                }
            }
            result.Add(cur.ToString().Trim());
            return result;
        }
    }

    public class AddressBookForm : Form
    {
        private List<RicohAddressEntry> entries = new List<RicohAddressEntry>();
        private List<PrinterConfig> printers;
        private DataGridView grid;
        private ComboBox cmbTargetPrinter;
        private TextBox txtPrinterIp;
        private TextBox txtAdminUser;
        private TextBox txtAdminPass;
        private Label lblCount;
        private Label lblStatus;
        private ProgressBar pbProgress;
        private Button btnRead;
        private Button btnPush;
        private Button btnClone;

        public AddressBookForm(List<PrinterConfig> availablePrinters)
        {
            this.printers = availablePrinters != null ? availablePrinters : new List<PrinterConfig>();
            InitializeAddressBookUI();
            PopulatePrinters();
            UpdateGrid();
        }

        private void InitializeAddressBookUI()
        {
            this.Text = "Ricoh MFP Scanner Address Book Manager";
            this.Size = new Size(1100, 720);
            this.MinimumSize = new Size(950, 580);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Header Banner
            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = 78;
            headerPanel.BackColor = Color.FromArgb(26, 32, 44);

            Panel accentBar = new Panel();
            accentBar.Dock = DockStyle.Top;
            accentBar.Height = 4;
            accentBar.BackColor = Color.FromArgb(79, 70, 229);
            headerPanel.Controls.Add(accentBar);

            Label lblTitle = new Label();
            lblTitle.Text = "📖 RICOH Scanner Address Book Manager";
            lblTitle.Font = new Font("Segoe UI", 13.5f, FontStyle.Bold);
            lblTitle.ForeColor = Color.White;
            lblTitle.Location = new Point(20, 16);
            lblTitle.AutoSize = true;
            headerPanel.Controls.Add(lblTitle);

            Label lblSub = new Label();
            lblSub.Text = "Direct Network Push / Pull • Bulk Scan-to-Email & Scan-to-Folder (SMB) • Backup & Multi-MFP Clone";
            lblSub.ForeColor = Color.FromArgb(148, 163, 184);
            lblSub.Location = new Point(23, 46);
            lblSub.AutoSize = true;
            headerPanel.Controls.Add(lblSub);

            this.Controls.Add(headerPanel);

            // Target Machine & Auth Card Panel
            Panel targetCard = new Panel();
            targetCard.Dock = DockStyle.Top;
            targetCard.Height = 72;
            targetCard.BackColor = Color.White;
            targetCard.Padding = new Padding(15, 12, 15, 10);
            targetCard.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, targetCard.ClientRectangle,
                    Color.FromArgb(226, 232, 240), ButtonBorderStyle.Solid);
            };

            FlowLayoutPanel flowTarget = new FlowLayoutPanel();
            flowTarget.Dock = DockStyle.Fill;
            flowTarget.FlowDirection = FlowDirection.LeftToRight;
            flowTarget.WrapContents = false;
            flowTarget.BackColor = Color.Transparent;

            Label lblP = new Label();
            lblP.Text = "Target MFP:";
            lblP.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblP.Margin = new Padding(0, 8, 4, 0);
            lblP.AutoSize = true;
            flowTarget.Controls.Add(lblP);

            cmbTargetPrinter = new ComboBox();
            cmbTargetPrinter.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbTargetPrinter.Width = 210;
            cmbTargetPrinter.Margin = new Padding(0, 5, 12, 0);
            cmbTargetPrinter.SelectedIndexChanged += (s, e) =>
            {
                if (cmbTargetPrinter.SelectedIndex >= 0 && cmbTargetPrinter.SelectedIndex < printers.Count)
                {
                    txtPrinterIp.Text = printers[cmbTargetPrinter.SelectedIndex].ip;
                }
            };
            flowTarget.Controls.Add(cmbTargetPrinter);

            Label lblIp = new Label();
            lblIp.Text = "IP:";
            lblIp.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblIp.Margin = new Padding(0, 8, 4, 0);
            lblIp.AutoSize = true;
            flowTarget.Controls.Add(lblIp);

            txtPrinterIp = new TextBox();
            txtPrinterIp.Width = 115;
            txtPrinterIp.Margin = new Padding(0, 5, 12, 0);
            flowTarget.Controls.Add(txtPrinterIp);

            Label lblUser = new Label();
            lblUser.Text = "Admin:";
            lblUser.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblUser.Margin = new Padding(0, 8, 4, 0);
            lblUser.AutoSize = true;
            flowTarget.Controls.Add(lblUser);

            txtAdminUser = new TextBox();
            txtAdminUser.Text = "admin";
            txtAdminUser.Width = 75;
            txtAdminUser.Margin = new Padding(0, 5, 12, 0);
            flowTarget.Controls.Add(txtAdminUser);

            Label lblPass = new Label();
            lblPass.Text = "Pass:";
            lblPass.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblPass.Margin = new Padding(0, 8, 4, 0);
            lblPass.AutoSize = true;
            flowTarget.Controls.Add(lblPass);

            txtAdminPass = new TextBox();
            txtAdminPass.UseSystemPasswordChar = true;
            txtAdminPass.Width = 75;
            txtAdminPass.Margin = new Padding(0, 5, 16, 0);
            flowTarget.Controls.Add(txtAdminPass);

            btnRead = CreateBtn("📥 Read Printer", Color.FromArgb(51, 65, 85), Color.White, 120, 32);
            btnRead.Click += (s, e) => ReadFromPrinter();
            flowTarget.Controls.Add(btnRead);

            btnPush = CreateBtn("🚀 Push to MFP", Color.FromArgb(16, 185, 129), Color.White, 120, 32);
            btnPush.Margin = new Padding(8, 0, 0, 0);
            btnPush.Click += (s, e) => PushToPrinter();
            flowTarget.Controls.Add(btnPush);

            btnClone = CreateBtn("📋 Clone / Copy", Color.FromArgb(2, 132, 199), Color.White, 115, 32);
            btnClone.Margin = new Padding(8, 0, 0, 0);
            btnClone.Click += (s, e) => OpenCloneDialog();
            flowTarget.Controls.Add(btnClone);

            targetCard.Controls.Add(flowTarget);
            this.Controls.Add(targetCard);

            // Toolbar Panel for contacts & file operations
            Panel toolBar = new Panel();
            toolBar.Dock = DockStyle.Top;
            toolBar.Height = 48;
            toolBar.BackColor = Color.FromArgb(241, 245, 249);
            toolBar.Padding = new Padding(15, 8, 15, 8);

            FlowLayoutPanel flowBtns = new FlowLayoutPanel();
            flowBtns.Dock = DockStyle.Fill;
            flowBtns.FlowDirection = FlowDirection.LeftToRight;
            flowBtns.WrapContents = false;
            flowBtns.BackColor = Color.Transparent;

            Button btnAdd = CreateBtn("➕ Add Contact", Color.FromArgb(207, 16, 45), Color.White, 125, 30);
            btnAdd.Click += (s, e) => AddContact();
            flowBtns.Controls.Add(btnAdd);

            Button btnEdit = CreateBtn("✏️ Edit", Color.FromArgb(71, 85, 105), Color.White, 75, 30);
            btnEdit.Margin = new Padding(6, 0, 0, 0);
            btnEdit.Click += (s, e) => EditSelectedContact();
            flowBtns.Controls.Add(btnEdit);

            Button btnDel = CreateBtn("🗑️ Delete", Color.FromArgb(254, 226, 226), Color.FromArgb(220, 38, 38), 85, 30);
            btnDel.Margin = new Padding(6, 0, 0, 0);
            btnDel.Click += (s, e) => DeleteSelectedContact();
            flowBtns.Controls.Add(btnDel);

            Button btnClear = CreateBtn("🧹 Clear", Color.FromArgb(226, 232, 240), Color.FromArgb(30, 41, 59), 75, 30);
            btnClear.Margin = new Padding(6, 0, 16, 0);
            btnClear.Click += (s, e) => ClearAllContacts();
            flowBtns.Controls.Add(btnClear);

            Button btnSaveProfile = CreateBtn("💾 Save Profile", Color.FromArgb(241, 245, 249), Color.FromArgb(30, 41, 59), 110, 30);
            btnSaveProfile.FlatAppearance.BorderSize = 1;
            btnSaveProfile.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnSaveProfile.Margin = new Padding(6, 0, 0, 0);
            btnSaveProfile.Click += (s, e) => SaveClientProfile();
            flowBtns.Controls.Add(btnSaveProfile);

            Button btnLoadProfile = CreateBtn("📂 Load Profile", Color.FromArgb(241, 245, 249), Color.FromArgb(30, 41, 59), 110, 30);
            btnLoadProfile.FlatAppearance.BorderSize = 1;
            btnLoadProfile.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnLoadProfile.Margin = new Padding(6, 0, 16, 0);
            btnLoadProfile.Click += (s, e) => LoadClientProfile();
            flowBtns.Controls.Add(btnLoadProfile);

            Button btnExpCsv = CreateBtn("📤 Export Ricoh CSV", Color.FromArgb(241, 245, 249), Color.FromArgb(30, 41, 59), 135, 30);
            btnExpCsv.FlatAppearance.BorderSize = 1;
            btnExpCsv.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnExpCsv.Margin = new Padding(6, 0, 0, 0);
            btnExpCsv.Click += (s, e) => ExportCsv();
            flowBtns.Controls.Add(btnExpCsv);

            Button btnImpCsv = CreateBtn("📥 Import CSV", Color.FromArgb(241, 245, 249), Color.FromArgb(30, 41, 59), 105, 30);
            btnImpCsv.FlatAppearance.BorderSize = 1;
            btnImpCsv.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnImpCsv.Margin = new Padding(6, 0, 0, 0);
            btnImpCsv.Click += (s, e) => ImportCsv();
            flowBtns.Controls.Add(btnImpCsv);

            toolBar.Controls.Add(flowBtns);
            this.Controls.Add(toolBar);

            // Bottom Status Strip
            Panel bottomStrip = new Panel();
            bottomStrip.Dock = DockStyle.Bottom;
            bottomStrip.Height = 36;
            bottomStrip.BackColor = Color.FromArgb(241, 245, 249);
            bottomStrip.Padding = new Padding(15, 6, 15, 6);

            lblCount = new Label();
            lblCount.Text = "Contacts: 0";
            lblCount.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblCount.ForeColor = Color.FromArgb(30, 41, 59);
            lblCount.Dock = DockStyle.Left;
            lblCount.AutoSize = true;
            bottomStrip.Controls.Add(lblCount);

            lblStatus = new Label();
            lblStatus.Text = "Ready";
            lblStatus.ForeColor = Color.FromArgb(71, 85, 105);
            lblStatus.Location = new Point(220, 8);
            lblStatus.AutoSize = true;
            bottomStrip.Controls.Add(lblStatus);

            pbProgress = new ProgressBar();
            pbProgress.Dock = DockStyle.Right;
            pbProgress.Width = 180;
            pbProgress.Style = ProgressBarStyle.Marquee;
            pbProgress.Visible = false;
            bottomStrip.Controls.Add(pbProgress);

            this.Controls.Add(bottomStrip);

            // DataGridView for Contacts
            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Color.FromArgb(241, 245, 249);
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.RowTemplate.Height = 32;
            grid.Font = new Font("Segoe UI", 9.25f);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(30, 41, 59);
            grid.ColumnHeadersHeight = 34;
            grid.EnableHeadersVisualStyles = false;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 231, 255);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 41, 59);

            grid.Columns.Add("colIdx", "#");
            grid.Columns[0].Width = 45;

            grid.Columns.Add("colKey", "Display Key (Button)");
            grid.Columns[1].Width = 150;

            grid.Columns.Add("colName", "Full Name");
            grid.Columns[2].Width = 150;

            grid.Columns.Add("colTitle", "Tab");
            grid.Columns[3].Width = 45;

            grid.Columns.Add("colEmail", "Scan-to-Email");
            grid.Columns[4].Width = 200;

            grid.Columns.Add("colProto", "Proto");
            grid.Columns[5].Width = 60;

            grid.Columns.Add("colFolder", "Scan-to-Folder (SMB / FTP)");
            grid.Columns[6].Width = 260;

            grid.Columns.Add("colUser", "Folder User");
            grid.Columns[7].Width = 110;

            grid.DoubleClick += (s, e) => EditSelectedContact();
            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) DeleteSelectedContact();
                else if (e.KeyCode == Keys.Enter) { EditSelectedContact(); e.Handled = true; }
            };

            this.Controls.Add(grid);
            grid.BringToFront();
        }

        private Button CreateBtn(string text, Color bg, Color fg, int width, int height)
        {
            Button b = new Button();
            b.Text = text;
            b.BackColor = bg;
            b.ForeColor = fg;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Size = new Size(width, height);
            b.Cursor = Cursors.Hand;
            b.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            return b;
        }

        private void PopulatePrinters()
        {
            cmbTargetPrinter.Items.Clear();
            foreach (var p in printers)
            {
                cmbTargetPrinter.Items.Add(p.displayName + " (" + p.ip + ")");
            }
            cmbTargetPrinter.Items.Add("[ Custom IP Address ]");

            if (printers.Count > 0)
            {
                cmbTargetPrinter.SelectedIndex = 0;
                txtPrinterIp.Text = printers[0].ip;
            }
            else
            {
                cmbTargetPrinter.SelectedIndex = 0;
                txtPrinterIp.Text = "192.168.1.200";
            }
        }

        private void UpdateGrid()
        {
            grid.Rows.Clear();
            int emailCount = 0;
            int folderCount = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                RicohAddressEntry e = entries[i];
                if (e.HasEmail) emailCount++;
                if (e.HasFolder) folderCount++;

                int rowId = grid.Rows.Add(
                    e.EntryIndex.ToString(),
                    e.DisplayKey,
                    e.Name,
                    e.TitleIndex,
                    e.HasEmail ? e.Email : "-",
                    e.HasFolder ? e.FolderProtocol : "-",
                    e.HasFolder ? e.FolderPath : "-",
                    e.HasFolder ? e.FolderUser : "-"
                );

                if (i % 2 == 1)
                {
                    grid.Rows[rowId].DefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
                }
            }

            lblCount.Text = string.Format("Total Contacts: {0} (Emails: {1}, Folders: {2})", entries.Count, emailCount, folderCount);
        }

        private void AddContact()
        {
            int nextIdx = entries.Count + 1;
            using (EditContactForm form = new EditContactForm(null, nextIdx))
            {
                if (form.ShowDialog(this) == DialogResult.OK && form.ResultEntry != null)
                {
                    entries.Add(form.ResultEntry);
                    UpdateGrid();
                    lblStatus.Text = "Added contact: " + form.ResultEntry.DisplayKey;
                }
            }
        }

        private void EditSelectedContact()
        {
            if (grid.SelectedRows.Count == 0) return;
            int idx = grid.SelectedRows[0].Index;
            if (idx < 0 || idx >= entries.Count) return;

            RicohAddressEntry cur = entries[idx];
            using (EditContactForm form = new EditContactForm(cur, cur.EntryIndex))
            {
                if (form.ShowDialog(this) == DialogResult.OK && form.ResultEntry != null)
                {
                    entries[idx] = form.ResultEntry;
                    UpdateGrid();
                    lblStatus.Text = "Updated contact: " + form.ResultEntry.DisplayKey;
                }
            }
        }

        private void DeleteSelectedContact()
        {
            if (grid.SelectedRows.Count == 0) return;
            int idx = grid.SelectedRows[0].Index;
            if (idx < 0 || idx >= entries.Count) return;

            string name = entries[idx].DisplayKey;
            DialogResult r = MessageBox.Show("Delete contact '" + name + "' from address book?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes)
            {
                entries.RemoveAt(idx);
                UpdateGrid();
                lblStatus.Text = "Deleted contact: " + name;
            }
        }

        private void ClearAllContacts()
        {
            if (entries.Count == 0) return;
            DialogResult r = MessageBox.Show("Are you sure you want to clear all " + entries.Count + " contacts?", "Clear All", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r == DialogResult.Yes)
            {
                entries.Clear();
                UpdateGrid();
                lblStatus.Text = "Address book cleared.";
            }
        }

        private void SaveClientProfile()
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "Address Book Profile (*.json)|*.json";
                sfd.Title = "Save Client Address Book Template";
                sfd.FileName = "Client_AddressBook.json";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        JavaScriptSerializer js = new JavaScriptSerializer();
                        File.WriteAllText(sfd.FileName, js.Serialize(entries), Encoding.UTF8);
                        MessageBox.Show("Saved " + entries.Count + " contacts to profile successfully!", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        lblStatus.Text = "Saved profile to: " + Path.GetFileName(sfd.FileName);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error saving profile: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void LoadClientProfile()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Address Book Profile (*.json)|*.json";
                ofd.Title = "Load Client Address Book Template";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        string json = File.ReadAllText(ofd.FileName, Encoding.UTF8);
                        JavaScriptSerializer js = new JavaScriptSerializer();
                        List<RicohAddressEntry> loaded = js.Deserialize<List<RicohAddressEntry>>(json);
                        if (loaded != null)
                        {
                            entries = loaded;
                            UpdateGrid();
                            MessageBox.Show("Loaded " + entries.Count + " contacts from profile!", "Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            lblStatus.Text = "Loaded profile: " + Path.GetFileName(ofd.FileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error reading profile: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ExportCsv()
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "Ricoh CSV File (*.csv)|*.csv";
                sfd.Title = "Export Ricoh Address Book CSV (Web Image Monitor Format)";
                sfd.FileName = "Ricoh_AddressBook.csv";
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        RicohCsvHelper.ExportRicohCsv(sfd.FileName, entries);
                        MessageBox.Show("Exported " + entries.Count + " contacts to standard Ricoh CSV!\n\nYou can upload this directly via Web Image Monitor:\n[Device Management] -> [Address Book] -> [Maintenance] -> [Import]", "CSV Exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        lblStatus.Text = "Exported CSV to: " + Path.GetFileName(sfd.FileName);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error exporting CSV: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ImportCsv()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*";
                ofd.Title = "Import Ricoh Address Book CSV";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        List<RicohAddressEntry> imported = RicohCsvHelper.ImportRicohCsv(ofd.FileName);
                        if (imported.Count > 0)
                        {
                            entries.AddRange(imported);
                            UpdateGrid();
                            MessageBox.Show("Imported " + imported.Count + " contacts successfully from CSV!", "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            lblStatus.Text = "Imported " + imported.Count + " contacts from " + Path.GetFileName(ofd.FileName);
                        }
                        else
                        {
                            MessageBox.Show("No contacts found in this CSV file.", "Empty", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error importing CSV: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ReadFromPrinter()
        {
            string ip = txtPrinterIp.Text.Trim();
            if (string.IsNullOrEmpty(ip))
            {
                MessageBox.Show("Please enter a valid Ricoh Printer IP address.", "Target IP Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string user = txtAdminUser.Text.Trim();
            string pass = txtAdminPass.Text;

            SetBusy(true, "Connecting to printer " + ip + " and querying address book...");

            Task.Factory.StartNew((Action)(() =>
            {
                try
                {
                    List<RicohAddressEntry> fetched = RicohSoapClient.ReadAddressBook(ip, user, pass);
                    this.Invoke((Action)(() =>
                    {
                        SetBusy(false, "Successfully read " + fetched.Count + " contacts from " + ip);
                        if (fetched.Count > 0)
                        {
                            DialogResult dr = MessageBox.Show(string.Format("Found {0} contacts on printer ({1}).\n\nDo you want to replace the current list?", fetched.Count, ip), "Address Book Read", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                            if (dr == DialogResult.Yes)
                            {
                                entries = fetched;
                                UpdateGrid();
                            }
                            else if (dr == DialogResult.No)
                            {
                                entries.AddRange(fetched);
                                UpdateGrid();
                            }
                        }
                        else
                        {
                            MessageBox.Show("Connection established, but no address book contacts were found on this printer.", "Empty Address Book", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke((Action)(() =>
                    {
                        SetBusy(false, "Failed to read from printer: " + ex.Message);
                        MessageBox.Show("Could not read address book from " + ip + ":\n\n" + ex.Message + "\n\nTip: Verify the machine IP, ensure the machine is powered on, and check admin credentials (default user: 'admin', empty password).", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            }));
        }

        private void PushToPrinter()
        {
            string ip = txtPrinterIp.Text.Trim();
            if (string.IsNullOrEmpty(ip))
            {
                MessageBox.Show("Please enter a valid Ricoh Printer IP address.", "Target IP Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (entries.Count == 0)
            {
                MessageBox.Show("There are no contacts in the list to push. Add contacts or load a profile first.", "No Contacts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirm = MessageBox.Show(string.Format("Are you sure you want to push all {0} contacts to Ricoh printer at {1}?", entries.Count, ip), "Confirm Direct Push", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            string user = txtAdminUser.Text.Trim();
            string pass = txtAdminPass.Text;

            SetBusy(true, "Pushing " + entries.Count + " contacts to printer " + ip + "...");

            Task.Factory.StartNew((Action)(() =>
            {
                try
                {
                    RicohSoapClient.PushAddressBook(ip, user, pass, entries);
                    this.Invoke((Action)(() =>
                    {
                        SetBusy(false, "Successfully pushed " + entries.Count + " contacts to " + ip + "!");
                        MessageBox.Show("Successfully pushed " + entries.Count + " contacts directly to Ricoh MFP (" + ip + ")!\n\nAll entries are now available on the machine scanner screen.", "Push Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke((Action)(() =>
                    {
                        SetBusy(false, "Failed to push to printer: " + ex.Message);
                        MessageBox.Show("Could not push address book to " + ip + ":\n\n" + ex.Message + "\n\nAlternative: You can use 'Export Ricoh CSV' and upload via Web Image Monitor.", "Push Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            }));
        }

        private void OpenCloneDialog()
        {
            using (ClonePrinterAddressForm form = new ClonePrinterAddressForm(printers, entries))
            {
                if (form.ShowDialog(this) == DialogResult.OK && form.ClonedEntries != null && form.ClonedEntries.Count > 0)
                {
                    entries = form.ClonedEntries;
                    UpdateGrid();
                }
            }
        }

        private void SetBusy(bool busy, string status)
        {
            lblStatus.Text = status;
            pbProgress.Visible = busy;
            btnRead.Enabled = !busy;
            btnPush.Enabled = !busy;
            btnClone.Enabled = !busy;
        }
    }

    public class EditContactForm : Form
    {
        private NumericUpDown nudIndex;
        private TextBox txtName;
        private TextBox txtKey;
        private TextBox txtTitle;

        private CheckBox chkEmail;
        private TextBox txtEmail;

        private CheckBox chkFolder;
        private ComboBox cmbProtocol;
        private TextBox txtFolderPath;
        private TextBox txtFolderUser;
        private TextBox txtFolderPass;
        private Button btnAutoFillSmb;

        private Button btnSave;
        private Button btnCancel;

        public RicohAddressEntry ResultEntry { get; private set; }

        public EditContactForm(RicohAddressEntry existing, int defaultIndex)
        {
            InitializeEditUI(existing, defaultIndex);
        }

        private void InitializeEditUI(RicohAddressEntry existing, int defaultIndex)
        {
            this.Text = existing == null ? "Add New Scanner Destination" : "Edit Scanner Destination";
            this.Size = new Size(580, 590);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            Label lblHead = new Label();
            lblHead.Text = existing == null ? "➕ Add Scanner Destination" : "✏️ Edit Destination";
            lblHead.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            lblHead.ForeColor = Color.FromArgb(15, 23, 42);
            lblHead.Location = new Point(20, 16);
            lblHead.AutoSize = true;
            this.Controls.Add(lblHead);

            // Row 1: Index & Full Name
            Label lblIdx = new Label();
            lblIdx.Text = "Reg #:";
            lblIdx.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblIdx.Location = new Point(22, 58);
            lblIdx.AutoSize = true;
            this.Controls.Add(lblIdx);

            nudIndex = new NumericUpDown();
            nudIndex.Minimum = 1;
            nudIndex.Maximum = 999;
            nudIndex.Value = existing != null ? existing.EntryIndex : defaultIndex;
            nudIndex.Location = new Point(78, 55);
            nudIndex.Width = 65;
            this.Controls.Add(nudIndex);

            Label lblName = new Label();
            lblName.Text = "Full Name:";
            lblName.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblName.Location = new Point(160, 58);
            lblName.AutoSize = true;
            this.Controls.Add(lblName);

            txtName = new TextBox();
            txtName.Text = existing != null ? existing.Name : "";
            txtName.Location = new Point(240, 55);
            txtName.Width = 300;
            txtName.TextChanged += (s, e) =>
            {
                if (existing == null || string.IsNullOrEmpty(txtKey.Text))
                {
                    txtKey.Text = txtName.Text;
                    if (!string.IsNullOrEmpty(txtName.Text))
                    {
                        txtTitle.Text = txtName.Text.Substring(0, 1).ToUpper();
                    }
                }
            };
            this.Controls.Add(txtName);

            // Row 2: Key Display & Title Tab
            Label lblKey = new Label();
            lblKey.Text = "Button Key:";
            lblKey.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblKey.Location = new Point(22, 98);
            lblKey.AutoSize = true;
            this.Controls.Add(lblKey);

            txtKey = new TextBox();
            txtKey.Text = existing != null ? existing.DisplayKey : "";
            txtKey.Location = new Point(110, 95);
            txtKey.Width = 240;
            this.Controls.Add(txtKey);

            Label lblTitle = new Label();
            lblTitle.Text = "Search Tab:";
            lblTitle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblTitle.Location = new Point(370, 98);
            lblTitle.AutoSize = true;
            this.Controls.Add(lblTitle);

            txtTitle = new TextBox();
            txtTitle.Text = existing != null ? existing.TitleIndex : "A";
            txtTitle.Location = new Point(460, 95);
            txtTitle.Width = 80;
            this.Controls.Add(txtTitle);

            // Group 1: Scan-to-Email
            GroupBox gbEmail = new GroupBox();
            gbEmail.Text = " 📧 Scan-to-Email Destination ";
            gbEmail.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            gbEmail.Location = new Point(20, 140);
            gbEmail.Size = new Size(524, 95);

            chkEmail = new CheckBox();
            chkEmail.Text = "Enable Scan-to-Email";
            chkEmail.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            chkEmail.Checked = existing != null ? existing.HasEmail : false;
            chkEmail.Location = new Point(20, 26);
            chkEmail.AutoSize = true;
            chkEmail.CheckedChanged += (s, e) => { txtEmail.Enabled = chkEmail.Checked; };
            gbEmail.Controls.Add(chkEmail);

            Label lblMail = new Label();
            lblMail.Text = "Email Address:";
            lblMail.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            lblMail.Location = new Point(20, 56);
            lblMail.AutoSize = true;
            gbEmail.Controls.Add(lblMail);

            txtEmail = new TextBox();
            txtEmail.Text = existing != null ? existing.Email : "";
            txtEmail.Enabled = chkEmail.Checked;
            txtEmail.Location = new Point(125, 53);
            txtEmail.Width = 375;
            gbEmail.Controls.Add(txtEmail);

            this.Controls.Add(gbEmail);

            // Group 2: Scan-to-Folder (SMB)
            GroupBox gbFolder = new GroupBox();
            gbFolder.Text = " 📂 Scan-to-Folder (SMB / FTP) Destination ";
            gbFolder.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            gbFolder.Location = new Point(20, 248);
            gbFolder.Size = new Size(524, 215);

            chkFolder = new CheckBox();
            chkFolder.Text = "Enable Scan-to-Folder";
            chkFolder.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            chkFolder.Checked = existing != null ? existing.HasFolder : false;
            chkFolder.Location = new Point(20, 26);
            chkFolder.AutoSize = true;
            chkFolder.CheckedChanged += (s, e) =>
            {
                bool en = chkFolder.Checked;
                cmbProtocol.Enabled = en;
                txtFolderPath.Enabled = en;
                txtFolderUser.Enabled = en;
                txtFolderPass.Enabled = en;
                btnAutoFillSmb.Enabled = en;
            };
            gbFolder.Controls.Add(chkFolder);

            Label lblProto = new Label();
            lblProto.Text = "Protocol:";
            lblProto.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            lblProto.Location = new Point(280, 27);
            lblProto.AutoSize = true;
            gbFolder.Controls.Add(lblProto);

            cmbProtocol = new ComboBox();
            cmbProtocol.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbProtocol.Items.AddRange(new object[] { "SMB", "FTP" });
            cmbProtocol.SelectedItem = (existing != null && !string.IsNullOrEmpty(existing.FolderProtocol)) ? existing.FolderProtocol : "SMB";
            cmbProtocol.Location = new Point(345, 24);
            cmbProtocol.Width = 155;
            cmbProtocol.Enabled = chkFolder.Checked;
            gbFolder.Controls.Add(cmbProtocol);

            Label lblPath = new Label();
            lblPath.Text = "Shared Path:";
            lblPath.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            lblPath.Location = new Point(20, 66);
            lblPath.AutoSize = true;
            gbFolder.Controls.Add(lblPath);

            txtFolderPath = new TextBox();
            txtFolderPath.Text = existing != null ? existing.FolderPath : "";
            txtFolderPath.Enabled = chkFolder.Checked;
            txtFolderPath.Location = new Point(125, 63);
            txtFolderPath.Width = 375;
            gbFolder.Controls.Add(txtFolderPath);

            btnAutoFillSmb = new Button();
            btnAutoFillSmb.Text = "⚡ Auto-Fill Path (\\\\ThisPC\\Scans\\User)";
            btnAutoFillSmb.BackColor = Color.FromArgb(224, 231, 255);
            btnAutoFillSmb.ForeColor = Color.FromArgb(67, 56, 202);
            btnAutoFillSmb.FlatStyle = FlatStyle.Flat;
            btnAutoFillSmb.FlatAppearance.BorderSize = 0;
            btnAutoFillSmb.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnAutoFillSmb.Location = new Point(125, 96);
            btnAutoFillSmb.Size = new Size(260, 26);
            btnAutoFillSmb.Cursor = Cursors.Hand;
            btnAutoFillSmb.Enabled = chkFolder.Checked;
            btnAutoFillSmb.Click += (s, e) =>
            {
                string user = string.IsNullOrEmpty(txtKey.Text) ? Environment.UserName : txtKey.Text.Trim();
                txtFolderPath.Text = "\\\\" + Environment.MachineName + "\\Scans\\" + user;
                if (string.IsNullOrEmpty(txtFolderUser.Text))
                {
                    txtFolderUser.Text = Environment.UserName;
                }
            };
            gbFolder.Controls.Add(btnAutoFillSmb);

            Label lblU = new Label();
            lblU.Text = "Login User:";
            lblU.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            lblU.Location = new Point(20, 136);
            lblU.AutoSize = true;
            gbFolder.Controls.Add(lblU);

            txtFolderUser = new TextBox();
            txtFolderUser.Text = existing != null ? existing.FolderUser : "";
            txtFolderUser.Enabled = chkFolder.Checked;
            txtFolderUser.Location = new Point(125, 133);
            txtFolderUser.Width = 375;
            gbFolder.Controls.Add(txtFolderUser);

            Label lblPwd = new Label();
            lblPwd.Text = "Password:";
            lblPwd.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            lblPwd.Location = new Point(20, 174);
            lblPwd.AutoSize = true;
            gbFolder.Controls.Add(lblPwd);

            txtFolderPass = new TextBox();
            txtFolderPass.UseSystemPasswordChar = true;
            txtFolderPass.Text = existing != null ? existing.FolderPassword : "";
            txtFolderPass.Enabled = chkFolder.Checked;
            txtFolderPass.Location = new Point(125, 171);
            txtFolderPass.Width = 375;
            gbFolder.Controls.Add(txtFolderPass);

            this.Controls.Add(gbFolder);

            // Bottom Actions
            btnSave = new Button();
            btnSave.Text = "💾 Save Destination";
            btnSave.BackColor = Color.FromArgb(16, 185, 129);
            btnSave.ForeColor = Color.White;
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnSave.Location = new Point(260, 485);
            btnSave.Size = new Size(160, 38);
            btnSave.Cursor = Cursors.Hand;
            btnSave.Click += (s, e) => SaveAndClose();
            this.Controls.Add(btnSave);

            btnCancel = new Button();
            btnCancel.Text = "Cancel";
            btnCancel.BackColor = Color.FromArgb(226, 232, 240);
            btnCancel.ForeColor = Color.FromArgb(51, 65, 85);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            btnCancel.Location = new Point(435, 485);
            btnCancel.Size = new Size(105, 38);
            btnCancel.Cursor = Cursors.Hand;
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);
        }

        private void SaveAndClose()
        {
            string name = txtName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Please enter a name for this contact.", "Name Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!chkEmail.Checked && !chkFolder.Checked)
            {
                MessageBox.Show("Please enable at least Scan-to-Email or Scan-to-Folder for this contact.", "Destination Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RicohAddressEntry entry = new RicohAddressEntry();
            entry.EntryIndex = (int)nudIndex.Value;
            entry.Name = name;
            entry.DisplayKey = string.IsNullOrEmpty(txtKey.Text.Trim()) ? name : txtKey.Text.Trim();
            entry.TitleIndex = string.IsNullOrEmpty(txtTitle.Text.Trim()) ? (name.Length > 0 ? name.Substring(0, 1).ToUpper() : "A") : txtTitle.Text.Trim();

            entry.HasEmail = chkEmail.Checked;
            entry.Email = txtEmail.Text.Trim();

            entry.HasFolder = chkFolder.Checked;
            entry.FolderProtocol = cmbProtocol.SelectedItem != null ? cmbProtocol.SelectedItem.ToString() : "SMB";
            entry.FolderPath = txtFolderPath.Text.Trim();
            entry.FolderUser = txtFolderUser.Text.Trim();
            entry.FolderPassword = txtFolderPass.Text;

            this.ResultEntry = entry;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }

    public class ClonePrinterAddressForm : Form
    {
        private List<PrinterConfig> printers;
        private List<RicohAddressEntry> currentEntries;
        private ComboBox cmbSource;
        private ComboBox cmbDest;
        private TextBox txtAdminUser;
        private TextBox txtAdminPass;
        private Label lblStatus;
        private ProgressBar pbProgress;
        private Button btnClone;

        public List<RicohAddressEntry> ClonedEntries { get; private set; }

        public ClonePrinterAddressForm(List<PrinterConfig> availablePrinters, List<RicohAddressEntry> existingEntries)
        {
            this.printers = availablePrinters != null ? availablePrinters : new List<PrinterConfig>();
            this.currentEntries = existingEntries;
            InitializeCloneUI();
        }

        private void InitializeCloneUI()
        {
            this.Text = "Clone Address Book Between Ricoh Printers";
            this.Size = new Size(540, 380);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            Label lblHead = new Label();
            lblHead.Text = "📋 Clone Address Book to Another MFP";
            lblHead.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            lblHead.ForeColor = Color.FromArgb(15, 23, 42);
            lblHead.Location = new Point(20, 16);
            lblHead.AutoSize = true;
            this.Controls.Add(lblHead);

            Label lblDesc = new Label();
            lblDesc.Text = "Reads all scan destinations from the Source printer and writes them directly\nto the Destination printer over the network.";
            lblDesc.ForeColor = Color.FromArgb(100, 116, 139);
            lblDesc.Location = new Point(22, 45);
            lblDesc.AutoSize = true;
            this.Controls.Add(lblDesc);

            Label lblSrc = new Label();
            lblSrc.Text = "Source Printer:";
            lblSrc.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblSrc.Location = new Point(25, 95);
            lblSrc.AutoSize = true;
            this.Controls.Add(lblSrc);

            cmbSource = new ComboBox();
            cmbSource.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSource.Location = new Point(145, 92);
            cmbSource.Width = 345;
            cmbSource.Items.Add("[ Current Address Book in App (" + currentEntries.Count + " contacts) ]");
            foreach (var p in printers) cmbSource.Items.Add(p.displayName + " (" + p.ip + ")");
            cmbSource.SelectedIndex = 0;
            this.Controls.Add(cmbSource);

            Label lblDst = new Label();
            lblDst.Text = "Destination Printer:";
            lblDst.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblDst.Location = new Point(25, 140);
            lblDst.AutoSize = true;
            this.Controls.Add(lblDst);

            cmbDest = new ComboBox();
            cmbDest.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbDest.Location = new Point(145, 137);
            cmbDest.Width = 345;
            foreach (var p in printers) cmbDest.Items.Add(p.displayName + " (" + p.ip + ")");
            if (cmbDest.Items.Count > 1) cmbDest.SelectedIndex = 1;
            else if (cmbDest.Items.Count > 0) cmbDest.SelectedIndex = 0;
            this.Controls.Add(cmbDest);

            Label lblU = new Label();
            lblU.Text = "Admin User:";
            lblU.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblU.Location = new Point(25, 185);
            lblU.AutoSize = true;
            this.Controls.Add(lblU);

            txtAdminUser = new TextBox();
            txtAdminUser.Text = "admin";
            txtAdminUser.Location = new Point(145, 182);
            txtAdminUser.Width = 140;
            this.Controls.Add(txtAdminUser);

            Label lblP = new Label();
            lblP.Text = "Password:";
            lblP.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblP.Location = new Point(300, 185);
            lblP.AutoSize = true;
            this.Controls.Add(lblP);

            txtAdminPass = new TextBox();
            txtAdminPass.UseSystemPasswordChar = true;
            txtAdminPass.Location = new Point(375, 182);
            txtAdminPass.Width = 115;
            this.Controls.Add(txtAdminPass);

            lblStatus = new Label();
            lblStatus.Text = "Ready to clone.";
            lblStatus.ForeColor = Color.FromArgb(71, 85, 105);
            lblStatus.Location = new Point(25, 230);
            lblStatus.AutoSize = true;
            this.Controls.Add(lblStatus);

            pbProgress = new ProgressBar();
            pbProgress.Location = new Point(25, 255);
            pbProgress.Width = 465;
            pbProgress.Height = 16;
            pbProgress.Style = ProgressBarStyle.Marquee;
            pbProgress.Visible = false;
            this.Controls.Add(pbProgress);

            btnClone = new Button();
            btnClone.Text = "🚀 Start Clone Now";
            btnClone.BackColor = Color.FromArgb(2, 132, 199);
            btnClone.ForeColor = Color.White;
            btnClone.FlatStyle = FlatStyle.Flat;
            btnClone.FlatAppearance.BorderSize = 0;
            btnClone.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnClone.Location = new Point(280, 285);
            btnClone.Size = new Size(210, 40);
            btnClone.Cursor = Cursors.Hand;
            btnClone.Click += (s, e) => ExecuteClone();
            this.Controls.Add(btnClone);

            Button btnClose = new Button();
            btnClose.Text = "Close";
            btnClose.BackColor = Color.FromArgb(226, 232, 240);
            btnClose.ForeColor = Color.FromArgb(51, 65, 85);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Location = new Point(175, 285);
            btnClose.Size = new Size(95, 40);
            btnClose.Cursor = Cursors.Hand;
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
        }

        private void ExecuteClone()
        {
            if (cmbDest.SelectedIndex < 0 || cmbDest.SelectedIndex >= printers.Count)
            {
                MessageBox.Show("Please select a valid Destination Printer.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string destIp = printers[cmbDest.SelectedIndex].ip;
            string user = txtAdminUser.Text.Trim();
            string pass = txtAdminPass.Text;

            btnClone.Enabled = false;
            pbProgress.Visible = true;

            Task.Factory.StartNew((Action)(() =>
            {
                try
                {
                    List<RicohAddressEntry> toPush;
                    if (cmbSource.SelectedIndex == 0)
                    {
                        toPush = currentEntries;
                    }
                    else
                    {
                        string srcIp = printers[cmbSource.SelectedIndex - 1].ip;
                        this.Invoke((Action)(() => { lblStatus.Text = "Reading contacts from source " + srcIp + "..."; }));
                        toPush = RicohSoapClient.ReadAddressBook(srcIp, user, pass);
                    }

                    if (toPush.Count == 0)
                    {
                        this.Invoke((Action)(() =>
                        {
                            btnClone.Enabled = true;
                            pbProgress.Visible = false;
                            lblStatus.Text = "No contacts found to clone.";
                            MessageBox.Show("Source address book contains 0 contacts. Nothing to clone.", "Empty Source", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }));
                        return;
                    }

                    this.Invoke((Action)(() => { lblStatus.Text = "Pushing " + toPush.Count + " contacts to destination " + destIp + "..."; }));
                    RicohSoapClient.PushAddressBook(destIp, user, pass, toPush);

                    this.Invoke((Action)(() =>
                    {
                        ClonedEntries = toPush;
                        btnClone.Enabled = true;
                        pbProgress.Visible = false;
                        lblStatus.Text = "Successfully cloned " + toPush.Count + " contacts to " + destIp + "!";
                        MessageBox.Show("Successfully cloned " + toPush.Count + " contacts to " + destIp + "!", "Clone Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke((Action)(() =>
                    {
                        btnClone.Enabled = true;
                        pbProgress.Visible = false;
                        lblStatus.Text = "Clone failed: " + ex.Message;
                        MessageBox.Show("Clone error:\n\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            }));
        }
    }
}
