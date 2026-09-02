# Third-party notices

MoniPaper's original source code, icon assets, and procedural textures are licensed under the MIT License in [LICENSE](LICENSE). Earlier releases used the name PaperCare.

The Windows x64 self-contained v1.0.0, v1.0.1, v1.1.0, v1.2.0, and v1.2.1 distributions include Microsoft .NET 10.0.8 components. Their copyright and license notices are retained separately and are not replaced by MoniPaper's license.

| Component | License and notices included in this distribution | Source |
| --- | --- | --- |
| .NET Runtime 10.0.8 | `licenses/dotnet-runtime-LICENSE.txt`, `licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt` | Files supplied by `Microsoft.NETCore.App.Runtime.win-x64` version 10.0.8; [upstream](https://github.com/dotnet/runtime/tree/v10.0.8) |
| .NET Windows Desktop Runtime 10.0.8 | `licenses/dotnet-desktop-LICENSE.txt` | File supplied by `Microsoft.WindowsDesktop.App.Runtime.win-x64` version 10.0.8; [upstream](https://github.com/dotnet/windowsdesktop/tree/v10.0.8) |
| WPF 10.0.8 | `licenses/wpf-THIRD-PARTY-NOTICES.txt` | [Upstream notice at v10.0.8](https://github.com/dotnet/wpf/blob/v10.0.8/THIRD-PARTY-NOTICES.TXT) |
| Windows Forms 10.0.8 | `licenses/winforms-THIRD-PARTY-NOTICES.txt` | [Upstream notice at v10.0.8](https://github.com/dotnet/winforms/blob/v10.0.8/THIRD-PARTY-NOTICES.TXT) |

WPF and Windows Forms are covered by the .NET Foundation license distributed with the desktop runtime. The upstream third-party notices are preserved in full and may describe components not used by every runtime configuration.

Keep `LICENSE`, this file, and the `licenses` directory with redistributed binaries. When building against a different runtime version, update the runtime notices to match that distribution.

MoniPaper does not include PaperMan code, artwork, icons, or textures. The reference to PaperMan in the README describes the inspiration for the project; it does not imply affiliation or endorsement.
