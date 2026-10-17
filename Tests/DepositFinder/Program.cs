using System;
using ChitakTech;
using KSP.UI.Screens;

internal static class Program
{
    static int checks;
    static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    static int Main()
    {
        try
        {
            var launcher = new ApplicationLauncher();
            ApplicationLauncher.Instance = launcher;
            ApplicationLauncher.Ready = true;
            var shared = new CTShared { depositFinder = true };
            shared.LauncherReady(); shared.Update();
            Check(launcher.Added == 0, "No button before main-menu initialization completes");
            shared.BeginMainMenuLoading();
            shared.LauncherReady();
            Check(!shared.DepositFinderEnabled, "Finder stays disabled during loading");
            shared.CompleteMainMenuLoading();
            Check(launcher.Added == 1, "Already-ready toolbar receives button after loading");
            shared.LauncherReady(); shared.Update();
            Check(launcher.Added == 1, "No duplicate button");
            var oldButton = shared.Button;
            oldButton.Open();
            Check(shared.Visible, "Enabled finder opens");
            shared.depositFinder = false;
            oldButton.Open();
            Check(!shared.Visible, "Stale callback cannot open a disabled finder");
            shared.Update();
            Check(launcher.Removed == 1 && shared.Button == null && !shared.Visible, "Disabling removes button and window");
            shared.LauncherReady();
            Check(launcher.Added == 1, "Disabled finder cannot reappear on launcher-ready event");
            shared.BeginMainMenuLoading(); shared.CompleteMainMenuLoading();
            Check(shared.Button == null, "False setting remains hidden after reload");
            shared.depositFinder = true; shared.Update(); shared.Button.Open();
            shared.BeginMainMenuLoading();
            Check(shared.Button == null && !shared.Visible, "Reload hides existing finder until loading completes");
            ApplicationLauncher.Ready = false;
            shared.CompleteMainMenuLoading();
            Check(shared.Button == null, "Wait for toolbar if settings load first");
            ApplicationLauncher.Ready = true; shared.LauncherReady();
            Check(shared.Button != null, "Later toolbar-ready event creates button");
            shared.Button.Open(); shared.LauncherDestroyed();
            Check(!shared.Visible && shared.Button == null, "Toolbar destruction closes window");
            ApplicationLauncher.Instance = new ApplicationLauncher(); shared.Update();
            Check(ApplicationLauncher.Instance.Added == 1, "Toolbar recreated once after scene change");
            var previous = ApplicationLauncher.Instance;
            ApplicationLauncher.Instance = new ApplicationLauncher(); shared.Update();
            Check(previous.Removed == 1 && ApplicationLauncher.Instance.Added == 1, "Replacement removes button from its owning toolbar");
            Console.WriteLine(checks + " deposit-finder assertions passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

namespace ChitakTech
{
    public partial class CTShared
    {
        public bool depositFinder;
        private bool _depositsVisible;
        private object _depositsButtonTexture = new object();
        private ApplicationLauncherButton _depositsToolbarButton;
        public bool Visible => _depositsVisible;
        public ApplicationLauncherButton Button => _depositsToolbarButton;
        public void LauncherReady() => OnDepositsLauncherReady();
        public void LauncherDestroyed() => OnDepositsLauncherDestroyed();
    }
}
namespace KSP.UI.Screens
{
    public sealed class ApplicationLauncherButton { public Action Open; }
    public sealed class ApplicationLauncher
    {
        public static ApplicationLauncher Instance;
        public static bool Ready;
        public int Added, Removed;
        public enum AppScenes { FLIGHT }
        public ApplicationLauncherButton AddModApplication(Action on, Action off, Action a, Action b,
            Action c, Action d, AppScenes scenes, object texture)
        { Added++; return new ApplicationLauncherButton { Open = on }; }
        public void RemoveModApplication(ApplicationLauncherButton button) { Removed++; }
    }
}
