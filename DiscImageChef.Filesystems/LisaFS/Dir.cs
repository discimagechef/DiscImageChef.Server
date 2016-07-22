﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : Dir.cs
// Version        : 1.0
// Author(s)      : Natalia Portillo
//
// Component      : Component
//
// Revision       : $Revision$
// Last change by : $Author$
// Date           : $Date$
//
// --[ Description ] ----------------------------------------------------------
//
// Description
//
// --[ License ] --------------------------------------------------------------
//
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU General Public License as
//     published by the Free Software Foundation, either version 3 of the
//     License, or (at your option) any later version.
//
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//
//     You should have received a copy of the GNU General Public License
//     along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
// ----------------------------------------------------------------------------
// Copyright (C) 2011-2015 Claunia.com
// ****************************************************************************/
// //$Id$
using System;
using System.Collections.Generic;
using DiscImageChef.ImagePlugins;

namespace DiscImageChef.Filesystems.LisaFS
{
    partial class LisaFS : Filesystem
    {
        public override Errno ReadLink(string path, ref string dest)
        {
            // LisaFS does not support symbolic links (afaik)
            return Errno.NotSupported;
        }

        public override Errno ReadDir(string path, ref List<string> contents)
        {
            Int16 fileId;
            bool isDir;
            Errno error = LookupFileId(path, out fileId, out isDir);
            if(error != Errno.NoError)
                return error;

            if(!isDir)
                return Errno.NotDirectory;

            List<CatalogEntry> catalog;
            ReadCatalog(fileId, out catalog);

            foreach(CatalogEntry entry in catalog)
                contents.Add(StringHandlers.CToString(entry.filename).Replace('/',':'));

            if(debug && fileId == FILEID_DIRECTORY)
            {
                contents.Add("$MDDF");
                contents.Add("$Boot");
                contents.Add("$Loader");
                contents.Add("$Bitmap");
                contents.Add("$S-Record");
                contents.Add("$");
            }

            contents.Sort();
            return Errno.NoError;
        }

        Errno ReadCatalog(Int16 fileId, out List<CatalogEntry> catalog)
        {
            catalog = null;

            if(!mounted)
                return Errno.AccessDenied;

            if(fileId < 4)
                return Errno.InvalidArgument;

            if(catalogCache.TryGetValue(fileId, out catalog))
                return Errno.NoError;

            int count = 0;

            // Catalogs don't have extents files so we need to traverse all disk searching pieces (tend to be non fragmented and non expandable)
            for(ulong i = 0; i < device.GetSectors(); i++)
            {
                byte[] tag = device.ReadSectorTag((ulong)i, SectorTagType.AppleSectorTag);
                UInt16 id = BigEndianBitConverter.ToUInt16(tag, 0x04);

                if(id == fileId)
                    count++;

                // Extents file found, it's not a catalog
                if(id == -fileId)
                    return Errno.NotDirectory;
            }

            if(count == 0)
                return Errno.NoSuchFile;

            byte[] buf = new byte[count * device.GetSectorSize()];

            // This can be enhanced to follow linked tags. However on some disks a linked tag cuts a file, better not let it do with a catalog
            for(ulong i = 0; i < device.GetSectors(); i++)
            {
                byte[] tag = device.ReadSectorTag((ulong)i, SectorTagType.AppleSectorTag);
                UInt16 id = BigEndianBitConverter.ToUInt16(tag, 0x04);

                if(id == fileId)
                {
                    UInt16 pos = BigEndianBitConverter.ToUInt16(tag, 0x06);
                    byte[] sector = device.ReadSector(i);
                    Array.Copy(sector, 0, buf, sector.Length * pos, sector.Length);
                }
            }

            int offset = 0;

            catalog = new List<CatalogEntry>();

            while((offset + 64) <= buf.Length)
            {
                if(buf[offset + 0x24] == 0x08)
                    offset += 78;
                else if(buf[offset + 0x24] == 0x7C)
                    offset += 50;
                else if(buf[offset + 0x24] == 0xFF)
                    break;
                else if(buf[offset + 0x24] == 0x03 && buf[offset] == 0x24)
                {
                    CatalogEntry entry = new CatalogEntry();
                    entry.marker = buf[offset];
                    entry.zero = BigEndianBitConverter.ToUInt16(buf, offset + 0x01);
                    entry.filename = new byte[E_NAME];
                    Array.Copy(buf, offset + 0x03, entry.filename, 0, E_NAME);
                    entry.padding = buf[offset + 0x23];
                    entry.fileType = buf[offset + 0x24];
                    entry.unknown = buf[offset + 0x25];
                    entry.fileID = BigEndianBitConverter.ToInt16(buf, offset + 0x26);
                    entry.dtc = BigEndianBitConverter.ToUInt32(buf, offset + 0x28);
                    entry.dtm = BigEndianBitConverter.ToUInt32(buf, offset + 0x2C);
                    entry.wasted = BigEndianBitConverter.ToInt32(buf, offset + 0x30);
                    entry.length = BigEndianBitConverter.ToInt32(buf, offset + 0x34);
                    entry.tail = new byte[8];
                    Array.Copy(buf, offset + 0x38, entry.tail, 0, 8);
                    catalog.Add(entry);

                    if(fileSizeCache.ContainsKey(entry.fileID))
                        fileSizeCache.Remove(entry.fileID);

                    fileSizeCache.Add(entry.fileID, entry.length);

                    offset += 64;
                }
                else
                    break;
            }

            catalogCache.Add(fileId, catalog);
            return Errno.NoError;
        }
    }
}
