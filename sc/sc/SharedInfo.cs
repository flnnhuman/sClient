using System;
using System.Reflection;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace sc
{
    public class SharedInfo
    {
        internal const string AssemblyName = "SClient";
        internal const string GithubRepo = "flnnhuman/" + AssemblyName;
        internal const string ProjectURL = "https://github.com/" + GithubRepo;
    
        internal static Version Version =>Version.Parse(AppInfo.VersionString);
        
        internal static string PublicIdentifier => AssemblyName;

    }
}