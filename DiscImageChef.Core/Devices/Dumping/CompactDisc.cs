﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : CompactDisc.cs
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
using System.IO;
using DiscImageChef.CommonTypes;
using DiscImageChef.Console;
using DiscImageChef.Core.Logging;
using DiscImageChef.Devices;
using Schemas;

namespace DiscImageChef.Core.Devices.Dumping
{
    internal class CompactDisc
    {
        internal static void Dump(Device dev, string devicePath, string outputPrefix, ushort retryPasses, bool force, bool dumpRaw, bool persistent, bool stopOnError, ref CICMMetadataType sidecar, ref MediaType dskType)
        {
            MHDDLog mhddLog;
            IBGLog ibgLog;
            byte[] cmdBuf = null;
            byte[] senseBuf = null;
            bool sense = false;
            double duration;
            ulong blocks = 0;
            uint blockSize = 0;
            byte[] tmpBuf;
            Decoders.CD.FullTOC.CDFullTOC? toc = null;
            DateTime start;
            DateTime end;
            double totalDuration = 0;
            double totalChkDuration = 0;
            double currentSpeed = 0;
            double maxSpeed = double.MinValue;
            double minSpeed = double.MaxValue;
            List<ulong> unreadableSectors = new List<ulong>();
            Checksum dataChk;
            bool readcd = false;
            byte[] readBuffer;
            uint blocksToRead = 64;
            ulong errored = 0;
            DataFile dumpFile = null;
            bool aborted = false;
            System.Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = aborted = true;
            };

            // We discarded all discs that falsify a TOC before requesting a real TOC
            // No TOC, no CD (or an empty one)
            bool tocSense = dev.ReadRawToc(out cmdBuf, out senseBuf, 1, dev.Timeout, out duration);
            if(!tocSense)
            {
                toc = Decoders.CD.FullTOC.Decode(cmdBuf);
                if(toc.HasValue)
                {
                    tmpBuf = new byte[cmdBuf.Length - 2];
                    Array.Copy(cmdBuf, 2, tmpBuf, 0, cmdBuf.Length - 2);
                    sidecar.OpticalDisc[0].TOC = new DumpType();
                    sidecar.OpticalDisc[0].TOC.Image = outputPrefix + ".toc.bin";
                    sidecar.OpticalDisc[0].TOC.Size = tmpBuf.Length;
                    sidecar.OpticalDisc[0].TOC.Checksums = Checksum.GetChecksums(tmpBuf).ToArray();
                    DataFile.WriteTo("SCSI Dump", sidecar.OpticalDisc[0].TOC.Image, tmpBuf);

                    // ATIP exists on blank CDs
                    sense = dev.ReadAtip(out cmdBuf, out senseBuf, dev.Timeout, out duration);
                    if(!sense)
                    {
                        Decoders.CD.ATIP.CDATIP? atip = Decoders.CD.ATIP.Decode(cmdBuf);
                        if(atip.HasValue)
                        {
                            if(blocks == 0)
                            {
                                DicConsole.ErrorWriteLine("Cannot dump blank media.");
                                return;
                            }

                            // Only CD-R and CD-RW have ATIP
                            dskType = atip.Value.DiscType ? MediaType.CDRW : MediaType.CDR;

                            tmpBuf = new byte[cmdBuf.Length - 4];
                            Array.Copy(cmdBuf, 4, tmpBuf, 0, cmdBuf.Length - 4);
                            sidecar.OpticalDisc[0].ATIP = new DumpType();
                            sidecar.OpticalDisc[0].ATIP.Image = outputPrefix + ".atip.bin";
                            sidecar.OpticalDisc[0].ATIP.Size = tmpBuf.Length;
                            sidecar.OpticalDisc[0].ATIP.Checksums = Checksum.GetChecksums(tmpBuf).ToArray();
                            DataFile.WriteTo("SCSI Dump", sidecar.OpticalDisc[0].TOC.Image, tmpBuf);
                        }
                    }

                    sense = dev.ReadDiscInformation(out cmdBuf, out senseBuf, MmcDiscInformationDataTypes.DiscInformation, dev.Timeout, out duration);
                    if(!sense)
                    {
                        Decoders.SCSI.MMC.DiscInformation.StandardDiscInformation? discInfo = Decoders.SCSI.MMC.DiscInformation.Decode000b(cmdBuf);
                        if(discInfo.HasValue)
                        {
                            // If it is a read-only CD, check CD type if available
                            if(dskType == MediaType.CD)
                            {
                                switch(discInfo.Value.DiscType)
                                {
                                    case 0x10:
                                        dskType = MediaType.CDI;
                                        break;
                                    case 0x20:
                                        dskType = MediaType.CDROMXA;
                                        break;
                                }
                            }
                        }
                    }

                    int sessions = 1;
                    int firstTrackLastSession = 0;

                    sense = dev.ReadSessionInfo(out cmdBuf, out senseBuf, dev.Timeout, out duration);
                    if(!sense)
                    {
                        Decoders.CD.Session.CDSessionInfo? session = Decoders.CD.Session.Decode(cmdBuf);
                        if(session.HasValue)
                        {
                            sessions = session.Value.LastCompleteSession;
                            firstTrackLastSession = session.Value.TrackDescriptors[0].TrackNumber;
                        }
                    }

                    if(dskType == MediaType.CD)
                    {
                        bool hasDataTrack = false;
                        bool hasAudioTrack = false;
                        bool allFirstSessionTracksAreAudio = true;
                        bool hasVideoTrack = false;

                        if(toc.HasValue)
                        {
                            foreach(Decoders.CD.FullTOC.TrackDataDescriptor track in toc.Value.TrackDescriptors)
                            {
                                if(track.TNO == 1 &&
                                    ((Decoders.CD.TOC_CONTROL)(track.CONTROL & 0x0D) == Decoders.CD.TOC_CONTROL.DataTrack ||
                                    (Decoders.CD.TOC_CONTROL)(track.CONTROL & 0x0D) == Decoders.CD.TOC_CONTROL.DataTrackIncremental))
                                {
                                    allFirstSessionTracksAreAudio &= firstTrackLastSession != 1;
                                }

                                if((Decoders.CD.TOC_CONTROL)(track.CONTROL & 0x0D) == Decoders.CD.TOC_CONTROL.DataTrack ||
                                    (Decoders.CD.TOC_CONTROL)(track.CONTROL & 0x0D) == Decoders.CD.TOC_CONTROL.DataTrackIncremental)
                                {
                                    hasDataTrack = true;
                                    allFirstSessionTracksAreAudio &= track.TNO >= firstTrackLastSession;
                                }
                                else
                                    hasAudioTrack = true;

                                hasVideoTrack |= track.ADR == 4;
                            }
                        }

                        if(hasDataTrack && hasAudioTrack && allFirstSessionTracksAreAudio && sessions == 2)
                            dskType = MediaType.CDPLUS;
                        if(!hasDataTrack && hasAudioTrack && sessions == 1)
                            dskType = MediaType.CDDA;
                        if(hasDataTrack && !hasAudioTrack && sessions == 1)
                            dskType = MediaType.CDROM;
                        if(hasVideoTrack && !hasDataTrack && sessions == 1)
                            dskType = MediaType.CDV;
                    }

                    sense = dev.ReadPma(out cmdBuf, out senseBuf, dev.Timeout, out duration);
                    if(!sense)
                    {
                        if(Decoders.CD.PMA.Decode(cmdBuf).HasValue)
                        {
                            tmpBuf = new byte[cmdBuf.Length - 4];
                            Array.Copy(cmdBuf, 4, tmpBuf, 0, cmdBuf.Length - 4);
                            sidecar.OpticalDisc[0].PMA = new DumpType();
                            sidecar.OpticalDisc[0].PMA.Image = outputPrefix + ".pma.bin";
                            sidecar.OpticalDisc[0].PMA.Size = tmpBuf.Length;
                            sidecar.OpticalDisc[0].PMA.Checksums = Checksum.GetChecksums(tmpBuf).ToArray();
                            DataFile.WriteTo("SCSI Dump", sidecar.OpticalDisc[0].PMA.Image, tmpBuf);
                        }
                    }

                    sense = dev.ReadCdText(out cmdBuf, out senseBuf, dev.Timeout, out duration);
                    if(!sense)
                    {
                        if(Decoders.CD.CDTextOnLeadIn.Decode(cmdBuf).HasValue)
                        {
                            tmpBuf = new byte[cmdBuf.Length - 4];
                            Array.Copy(cmdBuf, 4, tmpBuf, 0, cmdBuf.Length - 4);
                            sidecar.OpticalDisc[0].LeadInCdText = new DumpType();
                            sidecar.OpticalDisc[0].LeadInCdText.Image = outputPrefix + ".cdtext.bin";
                            sidecar.OpticalDisc[0].LeadInCdText.Size = tmpBuf.Length;
                            sidecar.OpticalDisc[0].LeadInCdText.Checksums = Checksum.GetChecksums(tmpBuf).ToArray();
                            DataFile.WriteTo("SCSI Dump", sidecar.OpticalDisc[0].LeadInCdText.Image, tmpBuf);
                        }
                    }
                }
            }

            //physicalBlockSize = 2448;

            if(toc == null)
            {
                DicConsole.ErrorWriteLine("Error trying to decode TOC...");
                return;
            }

            if(dumpRaw)
            {
                throw new NotImplementedException("Raw CD dumping not yet implemented");
            }
            else
            {
                // TODO: Check subchannel capabilities
                readcd = !dev.ReadCd(out readBuffer, out senseBuf, 0, 2448, 1, MmcSectorTypes.AllTypes, false, false, true, MmcHeaderCodes.AllHeaders,
                    true, true, MmcErrorField.None, MmcSubchannel.Raw, dev.Timeout, out duration);

                if(readcd)
                    DicConsole.WriteLine("Using MMC READ CD command.");
            }

            DicConsole.WriteLine("Trying to read Lead-In...");
            bool gotLeadIn = false;
            int leadInSectorsGood = 0, leadInSectorsTotal = 0;

            dumpFile = new DataFile(outputPrefix + ".leadin.bin");
            dataChk = new Checksum();

            start = DateTime.UtcNow;

            readBuffer = null;

            for(int leadInBlock = -150; leadInBlock < 0; leadInBlock++)
            {
                if(aborted)
                    break;

                double cmdDuration = 0;

#pragma warning disable RECS0018 // Comparison of floating point numbers with equality operator
                if(currentSpeed > maxSpeed && currentSpeed != 0)
                    maxSpeed = currentSpeed;
                if(currentSpeed < minSpeed && currentSpeed != 0)
                    minSpeed = currentSpeed;
#pragma warning restore RECS0018 // Comparison of floating point numbers with equality operator

                DicConsole.Write("\rTrying to read lead-in sector {0} ({1:F3} MiB/sec.)", leadInBlock, currentSpeed);

                sense = dev.ReadCd(out readBuffer, out senseBuf, (uint)leadInBlock, 2448, 1, MmcSectorTypes.AllTypes, false, false, true, MmcHeaderCodes.AllHeaders,
                    true, true, MmcErrorField.None, MmcSubchannel.Raw, dev.Timeout, out cmdDuration);

                if(!sense && !dev.Error)
                {
                    dataChk.Update(readBuffer);
                    dumpFile.Write(readBuffer);
                    gotLeadIn = true;
                    leadInSectorsGood++;
                    leadInSectorsTotal++;
                }
                else
                {
                    if(gotLeadIn)
                    {
                        // Write empty data
                        dataChk.Update(new byte[2448]);
                        dumpFile.Write(new byte[2448]);
                        leadInSectorsTotal++;
                    }
                }

#pragma warning disable IDE0004 // Remove Unnecessary Cast
                currentSpeed = ((double)2448 / (double)1048576) / (cmdDuration / (double)1000);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            }

            dumpFile.Close();
            if(leadInSectorsGood > 0)
            {
                sidecar.OpticalDisc[0].LeadIn = new BorderType[1];
                sidecar.OpticalDisc[0].LeadIn[0] = new BorderType();
                sidecar.OpticalDisc[0].LeadIn[0].Image = outputPrefix + ".leadin.bin";
                sidecar.OpticalDisc[0].LeadIn[0].Checksums = dataChk.End().ToArray();
                sidecar.OpticalDisc[0].LeadIn[0].Size = leadInSectorsTotal * 2448;
            }
            else
                File.Delete(outputPrefix + ".leadin.bin");

            DicConsole.WriteLine();
            DicConsole.WriteLine("Got {0} lead-in sectors.", leadInSectorsGood);

            while(true)
            {
                if(readcd)
                {
                    sense = dev.ReadCd(out readBuffer, out senseBuf, 0, 2448, blocksToRead, MmcSectorTypes.AllTypes, false, false, true, MmcHeaderCodes.AllHeaders,
                        true, true, MmcErrorField.None, MmcSubchannel.Raw, dev.Timeout, out duration);
                    if(dev.Error)
                        blocksToRead /= 2;
                }

                if(!dev.Error || blocksToRead == 1)
                    break;
            }

            if(dev.Error)
            {
                DicConsole.ErrorWriteLine("Device error {0} trying to guess ideal transfer length.", dev.LastError);
                return;
            }

            DicConsole.WriteLine("Reading {0} sectors at a time.", blocksToRead);

            dumpFile = new DataFile(outputPrefix + ".bin");
            mhddLog = new MHDDLog(outputPrefix + ".mhddlog.bin", dev, blocks, blockSize, blocksToRead);
            ibgLog = new IBGLog(outputPrefix + ".ibg", 0x0008);

            start = DateTime.UtcNow;
            for(ulong i = 0; i < blocks; i += blocksToRead)
            {
                if(aborted)
                    break;

                double cmdDuration = 0;

                if((blocks - i) < blocksToRead)
                    blocksToRead = (uint)(blocks - i);

#pragma warning disable RECS0018 // Comparison of floating point numbers with equality operator
                if(currentSpeed > maxSpeed && currentSpeed != 0)
                    maxSpeed = currentSpeed;
                if(currentSpeed < minSpeed && currentSpeed != 0)
                    minSpeed = currentSpeed;
#pragma warning restore RECS0018 // Comparison of floating point numbers with equality operator

                DicConsole.Write("\rReading sector {0} of {1} ({2:F3} MiB/sec.)", i, blocks, currentSpeed);

                if(readcd)
                {
                    sense = dev.ReadCd(out readBuffer, out senseBuf, (uint)i, 2448, blocksToRead, MmcSectorTypes.AllTypes, false, false, true, MmcHeaderCodes.AllHeaders,
                        true, true, MmcErrorField.None, MmcSubchannel.Raw, dev.Timeout, out cmdDuration);
                    totalDuration += cmdDuration;
                }

                if(!sense && !dev.Error)
                {
                    mhddLog.Write(i, cmdDuration);
                    ibgLog.Write(i, currentSpeed * 1024);
                    dumpFile.Write(readBuffer);
                }
                else
                {
                    // TODO: Reset device after X errors
                    if(stopOnError)
                        return; // TODO: Return more cleanly

                    // Write empty data
                    dumpFile.Write(new byte[2448 * blocksToRead]);

                    // TODO: Record error on mapfile

                    errored += blocksToRead;
                    unreadableSectors.Add(i);
                    DicConsole.DebugWriteLine("Dump-Media", "READ error:\n{0}", Decoders.SCSI.Sense.PrettifySense(senseBuf));
                    if(cmdDuration < 500)
                        mhddLog.Write(i, 65535);
                    else
                        mhddLog.Write(i, cmdDuration);

                    ibgLog.Write(i, 0);
                }

#pragma warning disable IDE0004 // Remove Unnecessary Cast
                currentSpeed = ((double)2448 * blocksToRead / (double)1048576) / (cmdDuration / (double)1000);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            }
            DicConsole.WriteLine();
            end = DateTime.UtcNow;
            mhddLog.Close();
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            ibgLog.Close(dev, blocks, blockSize, (end - start).TotalSeconds, currentSpeed * 1024, (((double)blockSize * (double)(blocks + 1)) / 1024) / (totalDuration / 1000), devicePath);
#pragma warning restore IDE0004 // Remove Unnecessary Cast

            #region Compact Disc Error handling
            if(unreadableSectors.Count > 0 && !aborted)
            {
                List<ulong> tmpList = new List<ulong>();

                foreach(ulong ur in unreadableSectors)
                {
                    for(ulong i = ur; i < ur + blocksToRead; i++)
                        tmpList.Add(i);
                }

                tmpList.Sort();

                int pass = 0;
                bool forward = true;
                bool runningPersistent = false;

                unreadableSectors = tmpList;

            cdRepeatRetry:
                ulong[] tmpArray = unreadableSectors.ToArray();
                foreach(ulong badSector in tmpArray)
                {
                    if(aborted)
                        break;

                    double cmdDuration = 0;

                    DicConsole.Write("\rRetrying sector {0}, pass {1}, {3}{2}", badSector, pass + 1, forward ? "forward" : "reverse", runningPersistent ? "recovering partial data, " : "");

                    if(readcd)
                    {
                        sense = dev.ReadCd(out readBuffer, out senseBuf, (uint)badSector, 2448, blocksToRead, MmcSectorTypes.AllTypes, false, false, true, MmcHeaderCodes.AllHeaders,
                            true, true, MmcErrorField.None, MmcSubchannel.Raw, dev.Timeout, out cmdDuration);
                        totalDuration += cmdDuration;
                    }

                    if(!sense && !dev.Error)
                    {
                        unreadableSectors.Remove(badSector);
                        dumpFile.WriteAt(readBuffer, badSector, blockSize);
                    }
                    else if(runningPersistent)
                        dumpFile.WriteAt(readBuffer, badSector, blockSize);
                }

                if(pass < retryPasses && !aborted && unreadableSectors.Count > 0)
                {
                    pass++;
                    forward = !forward;
                    unreadableSectors.Sort();
                    unreadableSectors.Reverse();
                    goto cdRepeatRetry;
                }

                Decoders.SCSI.Modes.DecodedMode? currentMode = null;
                Decoders.SCSI.Modes.ModePage? currentModePage = null;
                byte[] md6 = null;
                byte[] md10 = null;

                if(!runningPersistent && persistent)
                {
                    sense = dev.ModeSense6(out readBuffer, out senseBuf, false, ScsiModeSensePageControl.Current, 0x01, dev.Timeout, out duration);
                    if(sense)
                    {
                        sense = dev.ModeSense10(out readBuffer, out senseBuf, false, ScsiModeSensePageControl.Current, 0x01, dev.Timeout, out duration);
                        if(!sense)
                            currentMode = Decoders.SCSI.Modes.DecodeMode10(readBuffer, dev.SCSIType);
                    }
                    else
                        currentMode = Decoders.SCSI.Modes.DecodeMode6(readBuffer, dev.SCSIType);

                    if(currentMode.HasValue)
                        currentModePage = currentMode.Value.Pages[0];

                    Decoders.SCSI.Modes.ModePage_01_MMC pgMMC = new Decoders.SCSI.Modes.ModePage_01_MMC();
                    pgMMC.PS = false;
                    pgMMC.ReadRetryCount = 255;
                    pgMMC.Parameter = 0x20;

                    Decoders.SCSI.Modes.DecodedMode md = new Decoders.SCSI.Modes.DecodedMode();
                    md.Header = new Decoders.SCSI.Modes.ModeHeader();
                    md.Pages = new Decoders.SCSI.Modes.ModePage[1];
                    md.Pages[0] = new Decoders.SCSI.Modes.ModePage();
                    md.Pages[0].Page = 0x01;
                    md.Pages[0].Subpage = 0x00;
                    md.Pages[0].PageResponse = Decoders.SCSI.Modes.EncodeModePage_01_MMC(pgMMC);
                    md6 = Decoders.SCSI.Modes.EncodeMode6(md, dev.SCSIType);
                    md10 = Decoders.SCSI.Modes.EncodeMode10(md, dev.SCSIType);

                    sense = dev.ModeSelect(md6, out senseBuf, true, false, dev.Timeout, out duration);
                    if(sense)
                    {
                        sense = dev.ModeSelect10(md10, out senseBuf, true, false, dev.Timeout, out duration);
                    }

                    runningPersistent = true;
                    if(!sense && !dev.Error)
                    {
                        pass--;
                        goto cdRepeatRetry;
                    }
                }
                else if(runningPersistent && persistent && currentModePage.HasValue)
                {
                    Decoders.SCSI.Modes.DecodedMode md = new Decoders.SCSI.Modes.DecodedMode();
                    md.Header = new Decoders.SCSI.Modes.ModeHeader();
                    md.Pages = new Decoders.SCSI.Modes.ModePage[1];
                    md.Pages[0] = currentModePage.Value;
                    md6 = Decoders.SCSI.Modes.EncodeMode6(md, dev.SCSIType);
                    md10 = Decoders.SCSI.Modes.EncodeMode10(md, dev.SCSIType);

                    sense = dev.ModeSelect(md6, out senseBuf, true, false, dev.Timeout, out duration);
                    if(sense)
                    {
                        sense = dev.ModeSelect10(md10, out senseBuf, true, false, dev.Timeout, out duration);
                    }
                }

                DicConsole.WriteLine();
            }
            #endregion Compact Disc Error handling

            dataChk = new Checksum();
            dumpFile.Seek(0, SeekOrigin.Begin);
            blocksToRead = 500;

            for(ulong i = 0; i < blocks; i += blocksToRead)
            {
                if(aborted)
                    break;

                if((blocks - i) < blocksToRead)
                    blocksToRead = (uint)(blocks - i);

                DicConsole.Write("\rChecksumming sector {0} of {1} ({2:F3} MiB/sec.)", i, blocks, currentSpeed);

                DateTime chkStart = DateTime.UtcNow;
                byte[] dataToCheck = new byte[blockSize * blocksToRead];
                dumpFile.Read(dataToCheck, 0, (int)(blockSize * blocksToRead));
                dataChk.Update(dataToCheck);
                DateTime chkEnd = DateTime.UtcNow;

                double chkDuration = (chkEnd - chkStart).TotalMilliseconds;
                totalChkDuration += chkDuration;

#pragma warning disable IDE0004 // Remove Unnecessary Cast
                currentSpeed = ((double)blockSize * blocksToRead / (double)1048576) / (chkDuration / (double)1000);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            }
            DicConsole.WriteLine();
            dumpFile.Close();
            end = DateTime.UtcNow;

            // TODO: Correct this
            sidecar.OpticalDisc[0].Checksums = dataChk.End().ToArray();
            sidecar.OpticalDisc[0].DumpHardwareArray = new DumpHardwareType[1];
            sidecar.OpticalDisc[0].DumpHardwareArray[0] = new DumpHardwareType();
            sidecar.OpticalDisc[0].DumpHardwareArray[0].Extents = new ExtentType[1];
            sidecar.OpticalDisc[0].DumpHardwareArray[0].Extents[0] = new ExtentType();
            sidecar.OpticalDisc[0].DumpHardwareArray[0].Extents[0].Start = 0;
            sidecar.OpticalDisc[0].DumpHardwareArray[0].Extents[0].End = (int)(blocks - 1);
            sidecar.OpticalDisc[0].DumpHardwareArray[0].Manufacturer = dev.Manufacturer;
            sidecar.OpticalDisc[0].DumpHardwareArray[0].Model = dev.Model;
            sidecar.OpticalDisc[0].DumpHardwareArray[0].Revision = dev.Revision;
            sidecar.OpticalDisc[0].DumpHardwareArray[0].Software = Version.GetSoftwareType(dev.PlatformID);
            sidecar.OpticalDisc[0].Image = new ImageType();
            sidecar.OpticalDisc[0].Image.format = "Raw disk image (sector by sector copy)";
            sidecar.OpticalDisc[0].Image.Value = outputPrefix + ".bin";
            sidecar.OpticalDisc[0].Sessions = 1;
            sidecar.OpticalDisc[0].Tracks = new[] { 1 };
            sidecar.OpticalDisc[0].Track = new Schemas.TrackType[1];
            sidecar.OpticalDisc[0].Track[0] = new Schemas.TrackType();
            sidecar.OpticalDisc[0].Track[0].BytesPerSector = (int)blockSize;
            sidecar.OpticalDisc[0].Track[0].Checksums = sidecar.OpticalDisc[0].Checksums;
            sidecar.OpticalDisc[0].Track[0].EndSector = (long)(blocks - 1);
            sidecar.OpticalDisc[0].Track[0].Image = new ImageType();
            sidecar.OpticalDisc[0].Track[0].Image.format = "BINARY";
            sidecar.OpticalDisc[0].Track[0].Image.offset = 0;
            sidecar.OpticalDisc[0].Track[0].Image.offsetSpecified = true;
            sidecar.OpticalDisc[0].Track[0].Image.Value = sidecar.OpticalDisc[0].Image.Value;
            sidecar.OpticalDisc[0].Track[0].Sequence = new TrackSequenceType();
            sidecar.OpticalDisc[0].Track[0].Sequence.Session = 1;
            sidecar.OpticalDisc[0].Track[0].Sequence.TrackNumber = 1;
            sidecar.OpticalDisc[0].Track[0].Size = (long)(blocks * blockSize);
            sidecar.OpticalDisc[0].Track[0].StartSector = 0;
            sidecar.OpticalDisc[0].Track[0].TrackType1 = TrackTypeTrackType.mode1;
            sidecar.OpticalDisc[0].Dimensions = Metadata.Dimensions.DimensionsFromMediaType(dskType);
            string xmlDskTyp, xmlDskSubTyp;
            Metadata.MediaType.MediaTypeToString(dskType, out xmlDskTyp, out xmlDskSubTyp);
            sidecar.OpticalDisc[0].DiscType = xmlDskTyp;
            sidecar.OpticalDisc[0].DiscSubType = xmlDskSubTyp;
        }
    }
}