using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace Win_1337_Patch
{
    public partial class Form1 : Form
    {
        private string exe = String.Empty;
        private string f1337 = String.Empty;

        public Form1()
        {
            InitializeComponent();
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string ver = "v" + version.Major + "." + version.Minor;
            this.Text = "Win 1337 Apply Patch File " + ver + " Fork By @ramhaidar";
            linkdfox.Text = ver + " Fork Version";
        }

        private void set()
        {
            try
            {
                t1337.Text = Ellipsis.Compact(f1337, t1337, EllipsisFormat.Path);
                toolTip1.SetToolTip(t1337, f1337);

                string[] lines = File.ReadAllLines(f1337);
                if (!check_Symbol(lines[0]))
                    return;

                if (TryAutoFillExecutable())
                    return;

                // Preserve original case for file dialog and filter (fixes case sensitivity issues on Windows 10)
                // Reference: https://github.com/Deltafox79/Win_1337_Apply_Patch/issues/4
                string unfOriginal = lines[0].Substring(1).Trim();
                string nf = Path.GetFileName(unfOriginal);
                string ext = Path.GetExtension(unfOriginal);
                OpenFileDialog apriDialogoFile1 = new OpenFileDialog
                {
                    FileName = nf,
                    Filter = "File " + ext + "|" + nf,
                    FilterIndex = 0,
                    Title = "Select the file \"" + nf + "\" File..."
                };

                if (apriDialogoFile1.ShowDialog() == DialogResult.OK)
                {
                    SetExecutableTarget(apriDialogoFile1.FileName);
                }
                else
                {
                    t1337.Text = "Select a .1337 File...";
                    texe.Text = "Select the Exe/Dll to Patch...";
                    f1337 = String.Empty;
                    exe = String.Empty;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while setting up the file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool TryAutoFillExecutable()
        {
            if (PatchTargetResolver.TryGetAutoTarget(f1337, out string suggestedPath) && File.Exists(suggestedPath))
            {
                SetExecutableTarget(suggestedPath);
                return true;
            }
            return false;
        }

        private void SetExecutableTarget(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            exe = path;
            texe.Text = Ellipsis.Compact(path, texe, EllipsisFormat.Path);
            toolTip1.SetToolTip(texe, path);
        }

        private void t1337_DragDrop(object sender, DragEventArgs e)
        {
            try
            {
                f1337 = ((string[])e.Data.GetData(DataFormats.FileDrop, false))[0];
                set();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while processing drag and drop: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnSelect1337_Click(object sender, EventArgs e)
        {
            try
            {
                string initialDirectory = Directory.GetCurrentDirectory();
                if (!string.IsNullOrWhiteSpace(f1337))
                {
                    string previousFolder = Path.GetDirectoryName(f1337);
                    if (!string.IsNullOrWhiteSpace(previousFolder) && Directory.Exists(previousFolder))
                        initialDirectory = previousFolder;
                }

                using (OpenFileDialog apriDialogoFile1 = new OpenFileDialog
                {
                    Filter = "File 1337|*.*",
                    FilterIndex = 0,
                    Title = "Select the .1337 File...",
                    InitialDirectory = initialDirectory,
                    RestoreDirectory = true
                })
                {
                    if (apriDialogoFile1.ShowDialog() == DialogResult.OK)
                    {
                        f1337 = apriDialogoFile1.FileName;
                        set();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while selecting the .1337 file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnSelectExe_Click(object sender, EventArgs e)
        {
            try
            {
                string initialDirectory = Directory.GetCurrentDirectory();
                if (!string.IsNullOrWhiteSpace(exe))
                {
                    string savedDirectory = Path.GetDirectoryName(exe);
                    if (!string.IsNullOrWhiteSpace(savedDirectory) && Directory.Exists(savedDirectory))
                        initialDirectory = savedDirectory;
                }

                using (OpenFileDialog apriDialogoFile = new OpenFileDialog
                {
                    Filter = "Executable or DLL|*.exe;*.dll|All Files|*.*",
                    FilterIndex = 0,
                    Title = "Select the Exe/Dll file to patch...",
                    InitialDirectory = initialDirectory,
                    RestoreDirectory = true
                })
                {
                    if (apriDialogoFile.ShowDialog() == DialogResult.OK)
                        SetExecutableTarget(apriDialogoFile.FileName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while selecting the Exe/Dll file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void t1337_DragEnter(object sender, DragEventArgs e)
        {
            try
            {
                e.Effect = DragDropEffects.All;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during DragEnter: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool check_Symbol(string s)
        {
            if (!s.StartsWith(">"))
            {
                MessageBox.Show("The .1337 File is not valid...", "Info...", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }

        private void Patch_Click(object sender, EventArgs e)
        {
            if (f1337 == String.Empty)
            {
                MessageBox.Show("Select a .1337 File...", "Info...", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            try
            {
                DFoX_Patch();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"A problem occurred when patching: {ex.Message}", "Info...", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DFoX_Patch()
        {
            if (string.IsNullOrWhiteSpace(f1337) || string.IsNullOrWhiteSpace(exe))
            {
                MessageBox.Show("Select a .1337 File and the Exe/Dll to Patch...", "Info...", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!File.Exists(exe) || !File.Exists(f1337))
            {
                MessageBox.Show("Files are no Longer Present...", "Info...", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var request = new PatchRequest(f1337, exe, cfileoffsett.Checked, controlloBackup.Checked, cchangeOwnership.Checked);
            var outcome = PatchEngine.ApplyPatch(request);

            if (outcome.Success)
                MessageBox.Show(outcome.Message, "Info...", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(outcome.Message, "Info...", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void DFoX_Load(object sender, EventArgs e)
        {
            try
            {
                cfileoffsett.Checked = (bool)Properties.Settings.Default["fixoffset"];
                controlloBackup.Checked = (bool)Properties.Settings.Default["backup"];
                cchangeOwnership.Checked = (bool)Properties.Settings.Default["changeOwnership"];

                exe = String.Empty;
                f1337 = String.Empty;
                texe.Text = "Select the Exe/Dll to Patch...";
                toolTip1.SetToolTip(texe, texe.Text);
                t1337.Text = "Select a .1337 File...";
                toolTip1.SetToolTip(t1337, t1337.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during form load: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void cfileoffsett_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                Properties.Settings.Default["fixoffset"] = cfileoffsett.Checked;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while saving fix offset setting: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void controlloBackup_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                Properties.Settings.Default["backup"] = controlloBackup.Checked;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while saving backup setting: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void cchangeOwnership_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                Properties.Settings.Default["changeOwnership"] = cchangeOwnership.Checked;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while saving change ownership setting: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void linkdfox_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(@"https://github.com/ramhaidar/Win_1337_Apply_Patch")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while opening the URL: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void t1337_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                btnSelect1337.PerformClick();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during double click: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

    }
}
