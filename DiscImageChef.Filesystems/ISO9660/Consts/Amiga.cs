﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : Amiga.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : Component
//
// --[ Description ] ----------------------------------------------------------
//
//     Description
//
// --[ License ] --------------------------------------------------------------
//
//     This library is free software; you can redistribute it and/or modify
//     it under the terms of the GNU Lesser General Public License as
//     published by the Free Software Foundation; either version 2.1 of the
//     License, or (at your option) any later version.
//
//     This library is distributed in the hope that it will be useful, but
//     WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//     Lesser General Public License for more details.
//
//     You should have received a copy of the GNU Lesser General Public
//     License along with this library; if not, see <http://www.gnu.org/licenses/>.
//
// ----------------------------------------------------------------------------
// Copyright © 2011-2017 Natalia Portillo
// ****************************************************************************/
using System;

namespace DiscImageChef.Filesystems.ISO9660
{
    public partial class ISO9660 : Filesystem
    {
        const ushort Amiga_Magic = 0x4153; // "AS"

        [Flags]
        enum AmigaFlags : byte
        {
            Protection = 1 << 0,
            Comment = 1 << 1,
            CommentContinues = 1 << 2
        }

        [Flags]
        enum AmigaMultiuser : byte
        {
            GroupDelete = 1 << 0,
            GroupExec = 1 << 1,
            GroupWrite = 1 << 2,
            GroupRead = 1 << 3,
            OtherDelete = 1 << 4,
            OtherExec = 1 << 5,
            OtherWrite = 1 << 6,
            OtherRead = 1 << 7,
        }

        [Flags]
        enum AmigaAttributes : byte
        {
            OwnerDelete = 1 << 0,
            OwnerExec = 1 << 1,
            OwnerWrite = 1 << 2,
            OwnerRead = 1 << 3,
            Archive = 1 << 4,
            Reentrant = 1 << 5,
            Script = 1 << 6,
            Reserved = 1 << 7,
        }
    }
}