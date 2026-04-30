using System;

namespace CodeSketch.Installer.Runtime
{
    public interface IUPMPackageAsset
    {
        UPMInstallEntry ToEntry();
        bool IsEssential { get; set; }
    }
}
