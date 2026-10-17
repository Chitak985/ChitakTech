using KSP.UI.Screens;

namespace ChitakTech
{
    public partial class CTShared
    {
        private bool _mainMenuLoadingComplete;
        private ApplicationLauncher _depositsLauncher;
        internal bool DepositFinderEnabled => _mainMenuLoadingComplete && depositFinder;

        internal void BeginMainMenuLoading()
        {
            _mainMenuLoadingComplete = false;
            RemoveDepositFinderButton();
        }

        internal void CompleteMainMenuLoading()
        {
            _mainMenuLoadingComplete = true;
            RefreshDepositFinder();
        }

        public void Update()
        {
            // Also close an already-open window if the flag is changed at runtime.
            RefreshDepositFinder();
        }

        private void RefreshDepositFinder()
        {
            if (!DepositFinderEnabled) RemoveDepositFinderButton();
            else if (ApplicationLauncher.Ready) OnDepositsLauncherReady();
        }

        private void OnDepositsLauncherReady()
        {
            if (!DepositFinderEnabled) { RemoveDepositFinderButton(); return; }
            var launcher = ApplicationLauncher.Instance;
            if (launcher == null || _depositsButtonTexture == null) return;
            if (_depositsToolbarButton != null && _depositsLauncher == launcher) return;
            RemoveDepositFinderButton();
            _depositsLauncher = launcher;
            _depositsToolbarButton = launcher.AddModApplication(
                () => _depositsVisible = DepositFinderEnabled,
                () => _depositsVisible = false,
                null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT, _depositsButtonTexture);
        }

        private void RemoveDepositFinderButton()
        {
            _depositsVisible = false;
            if (_depositsToolbarButton != null && _depositsLauncher != null)
                _depositsLauncher.RemoveModApplication(_depositsToolbarButton);
            _depositsToolbarButton = null;
            _depositsLauncher = null;
        }

        private void OnDepositsLauncherDestroyed()
        {
            _depositsToolbarButton = null;
            _depositsLauncher = null;
            _depositsVisible = false;
        }
    }
}
