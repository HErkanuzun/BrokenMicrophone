using System;
using System.Threading;
using System.Windows.Forms;

namespace BrokenMicrophone
{
    static class Program
    {
        const string MutexName = "BrokenMicrophone_SingleInstance";
        const string ShowEvent = "BrokenMicrophone_Show", ExitEvent = "BrokenMicrophone_Exit";

        static bool Signal(string name)
        {
            try { using (var e = EventWaitHandle.OpenExisting(name)) return e.Set(); }
            catch { return false; }
        }

        [STAThread]
        static void Main(string[] args)
        {
            // sanal mikrofonun adını değiştirmek için yönetici olarak açılan yardımcı kopya
            if (args.Length >= 3 && args[0] == "--rename-mic")
            {
                Environment.Exit(MicName.RunElevated(args[1], args[2], args.Length >= 4 ? args[3] : null));
                return;
            }

            // Kurulum paketi dosyaları değiştirmeden önce çalışan kopyayı düzgünce kapatır
            if (Array.IndexOf(args, "--exit") >= 0)
            {
                if (Signal(ExitEvent))
                    using (var m = new Mutex(false, MutexName))
                    {
                        try { if (m.WaitOne(30000)) m.ReleaseMutex(); }
                        catch (AbandonedMutexException) { }
                    }
                Environment.Exit(0);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var cfg = Settings.Load();
            if (cfg.Lang != "") L.Set(cfg.Lang);

            // "Yönetici olarak yeniden aç" ile başlatılan kopya, eskisinin kapanmasını bekler
            bool elevated = Array.IndexOf(args, "--elevated") >= 0;
            using (var mtx = new Mutex(false, MutexName))
            {
                bool owned;
                try { owned = mtx.WaitOne(elevated ? 15000 : 0); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned)
                {
                    // Zaten açık: o pencere öne getirilir
                    if (!Signal(ShowEvent))
                        MessageBox.Show(L.T("Broken Microphone zaten açık.", "Broken Microphone is already running."), AppInfo.Name);
                    return;
                }

                try
                {
                    using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEvent))
                    using (var exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEvent))
                    {
                        var form = new MainForm(cfg);
                        var listener = new Thread(delegate ()
                        {
                            var handles = new WaitHandle[] { show, exit };
                            while (true)
                            {
                                int i;
                                try { i = WaitHandle.WaitAny(handles); } catch { return; }
                                try
                                {
                                    if (i == 0) form.BeginInvoke((MethodInvoker)form.BringToFront2);
                                    else form.BeginInvoke((MethodInvoker)form.RequestExit);
                                }
                                catch { return; }
                            }
                        });
                        listener.IsBackground = true;
                        listener.Start();
                        Application.Run(form);
                    }
                }
                finally { mtx.ReleaseMutex(); }
            }
        }
    }
}
