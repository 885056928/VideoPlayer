# Third-Party License Notices

This directory is copied into every self-contained `win-x64` folder publish.
`scripts/acceptance/Publish-Release.ps1` also copies license, notice,
copyright, and copying files supplied by NuGet packages to
`LICENSES/packages/<package>/<version>/` and generates
`LICENSES/PACKAGE-NOTICES.txt` from the published dependency graph.

## V1.1 Required Package Versions

| Package | Version | License | Attribution |
|---|---:|---|---|
| LibVLCSharp.WPF | 3.10.1 | LGPL-2.1-or-later | VideoLAN |
| VideoLAN.LibVLC.Windows | 3.0.23.1 | LGPL-2.1-or-later | VideoLAN |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | Microsoft |
| Microsoft.Extensions.DependencyInjection | 10.0.12 | MIT | Microsoft |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.12 | MIT | Microsoft |
| Serilog | 4.4.0 | Apache-2.0 | Serilog Contributors |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | Serilog Contributors |

## LibVLC LGPL Notice

LibVLC, VLC, LibVLCSharp, LibVLCSharp.WPF, and
VideoLAN.LibVLC.Windows are Copyright (C) VideoLAN and their
respective contributors. These components are free software distributed
under the GNU Lesser General Public License, version 2.1 or, at your
option, any later version.

This library is free software; you can redistribute it and/or modify it
under the terms of the GNU Lesser General Public License as published by
the Free Software Foundation; either version 2.1 of the License, or (at
your option) any later version.

This library is distributed in the hope that it will be useful, but
WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Lesser
General Public License for more details.

You should have received a copy of the GNU Lesser General Public License
along with this library. If not, see
<https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html> or
<https://licenses.nuget.org/LGPL-2.1-or-later>.

The application dynamically uses the unmodified LibVLC native binaries
from `VideoLAN.LibVLC.Windows`. No GPL-only VLC modules are statically
linked into the application. Native files and notices supplied by the
NuGet package must remain with the published folder.

## Other Package Notices

MIT-licensed packages retain their copyright and permission notices in
the files supplied by their NuGet packages. Apache-2.0 packages retain
their `NOTICE` and license materials when supplied. Do not remove this
directory or the generated `PACKAGE-NOTICES.txt` file from a release.