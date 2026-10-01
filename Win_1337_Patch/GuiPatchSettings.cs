namespace Win_1337_Patch
{
    internal interface IGuiPatchSettings
    {
        bool FixOffset { get; set; }
        bool CreateBackup { get; set; }
        bool SavedOwnership { get; }
        void Save();
    }

    internal sealed class GuiPatchSettings : IGuiPatchSettings
    {
        public bool FixOffset
        {
            get => Properties.Settings.Default.fixoffset;
            set => Properties.Settings.Default.fixoffset = value;
        }
        public bool CreateBackup
        {
            get => Properties.Settings.Default.backup;
            set => Properties.Settings.Default.backup = value;
        }
        public bool SavedOwnership => Properties.Settings.Default.changeOwnership;
        public void Save() => Properties.Settings.Default.Save();
    }
}
