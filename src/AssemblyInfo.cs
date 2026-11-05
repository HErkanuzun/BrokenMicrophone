using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

// exe'nin "Özellikler > Ayrıntılar" bilgileri
[assembly: AssemblyTitle("Broken Microphone")]
[assembly: AssemblyDescription("Use your phone or tablet as the computer's microphone")]
[assembly: AssemblyCompany("HErkanuzun")]
[assembly: AssemblyProduct("Broken Microphone")]
[assembly: AssemblyCopyright("Copyright (c) 2026 HErkanuzun. MIT License.")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]
[assembly: AssemblyInformationalVersion("1.2")]
[assembly: NeutralResourcesLanguage("en")]
[assembly: ComVisible(false)]

namespace BrokenMicrophone
{
    static class AppInfo
    {
        public const string Name = "Broken Microphone";
        public const string Version = "1.2";
        public const string Repo = "https://github.com/HErkanuzun/BrokenMicrophone";
        // Android uygulamasının indirme sayfası (GitHub Pages) ve en son APK
        public const string AndroidPage = "https://herkanuzun.github.io/BrokenMicrophone/";
        public const string AndroidApk = Repo + "/releases/latest/download/BrokenMicrophone.apk";
    }
}
