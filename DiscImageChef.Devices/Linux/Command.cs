﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : Command.cs
// Version        : 1.0
// Author(s)      : Natalia Portillo
//
// Component      : Linux direct device access
//
// Revision       : $Revision$
// Last change by : $Author$
// Date           : $Date$
//
// --[ Description ] ----------------------------------------------------------
//
// Contains a high level representation of the Linux syscalls used to directly
// interface devices
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
using System.Runtime.InteropServices;

namespace DiscImageChef.Devices.Linux
{
    static class Command
    {
        /// <summary>
        /// Sends a SCSI command
        /// </summary>
        /// <returns>0 if no error occurred, otherwise, errno</returns>
        /// <param name="fd">File handle</param>
        /// <param name="cdb">SCSI CDB</param>
        /// <param name="buffer">Buffer for SCSI command response</param>
        /// <param name="senseBuffer">Buffer with the SCSI sense</param>
        /// <param name="timeout">Timeout in seconds</param>
        /// <param name="direction">SCSI command transfer direction</param>
        /// <param name="duration">Time it took to execute the command in milliseconds</param>
        /// <param name="sense"><c>True</c> if SCSI error returned non-OK status and <paramref name="senseBuffer"/> contains SCSI sense</param>
        internal static int SendScsiCommand(int fd, byte[] cdb, ref byte[] buffer, out byte[] senseBuffer, uint timeout, ScsiIoctlDirection direction, out double duration, out bool sense)
        {
            senseBuffer = null;
            duration = 0;
            sense = false;

            if (buffer == null)
                return -1;

            sg_io_hdr_t io_hdr = new sg_io_hdr_t();

            senseBuffer = new byte[32];

            io_hdr.interface_id = 'S';
            io_hdr.cmd_len = (byte)cdb.Length;
            io_hdr.mx_sb_len = (byte)senseBuffer.Length;
            io_hdr.dxfer_direction = direction;
            io_hdr.dxfer_len = (uint)buffer.Length;
            io_hdr.dxferp = Marshal.AllocHGlobal(buffer.Length);
            io_hdr.cmdp = Marshal.AllocHGlobal(cdb.Length);
            io_hdr.sbp = Marshal.AllocHGlobal(senseBuffer.Length);
            io_hdr.timeout = timeout * 1000;

            Marshal.Copy(buffer, 0, io_hdr.dxferp, buffer.Length);
            Marshal.Copy(cdb, 0, io_hdr.cmdp, cdb.Length);
            Marshal.Copy(senseBuffer, 0, io_hdr.sbp, senseBuffer.Length);

            int error = Extern.ioctlSg(fd, LinuxIoctl.SG_IO, ref io_hdr);

            if (error < 0)
                error = Marshal.GetLastWin32Error();

            Marshal.Copy(io_hdr.dxferp, buffer, 0, buffer.Length);
            Marshal.Copy(io_hdr.cmdp, cdb, 0, cdb.Length);
            Marshal.Copy(io_hdr.sbp, senseBuffer, 0, senseBuffer.Length);

            sense |= (io_hdr.info & SgInfo.OkMask) != SgInfo.Ok;

            duration = (double)io_hdr.duration;

            return error;
        }

        static ScsiIoctlDirection AtaProtocolToScsiDirection(Enums.AtaProtocol protocol)
        {
            switch (protocol)
            {
                case Enums.AtaProtocol.DeviceDiagnostic:
                case Enums.AtaProtocol.DeviceReset:
                case Enums.AtaProtocol.HardReset:
                case Enums.AtaProtocol.NonData:
                case Enums.AtaProtocol.SoftReset:
                case Enums.AtaProtocol.ReturnResponse:
                    return ScsiIoctlDirection.None;
                case Enums.AtaProtocol.PioIn:
                case Enums.AtaProtocol.UDmaIn:
                    return ScsiIoctlDirection.Out;
                case Enums.AtaProtocol.PioOut:
                case Enums.AtaProtocol.UDmaOut:
                    return ScsiIoctlDirection.In;
                default:
                    return ScsiIoctlDirection.Unspecified;
            }
        }

        internal static int SendAtaCommand(int fd, Structs.AtaRegistersCHS registers,
            out Structs.AtaErrorRegistersCHS errorRegisters, Enums.AtaProtocol protocol,
            Enums.AtaTransferRegister transferRegister, ref byte[] buffer, uint timeout,
            bool transferBlocks, out double duration, out bool sense)
        {
            duration = 0;
            sense = false;
            errorRegisters = new Structs.AtaErrorRegistersCHS();

            if (buffer == null)
                return -1;

            byte[] cdb = new byte[12];
            cdb[0] = (byte)Enums.ScsiCommands.AtaPassThrough;
            cdb[1] = (byte)(((byte)protocol << 1) & 0x1E);
            if (transferRegister != Enums.AtaTransferRegister.NoTransfer &&
               protocol != Enums.AtaProtocol.NonData)
            {
                switch (protocol)
                {
                    case Enums.AtaProtocol.PioIn:
                    case Enums.AtaProtocol.UDmaIn:
                        cdb[2] = 0x08;
                        break;
                    default:
                        cdb[2] = 0x00;
                        break;
                }

                if (transferBlocks)
                    cdb[2] |= 0x04;

                cdb[2] |= (byte)((int)transferRegister & 0x03);
            }

            cdb[3] = registers.feature;
            cdb[4] = registers.sectorCount;
            cdb[5] = registers.sector;
            cdb[6] = registers.cylinderHigh;
            cdb[7] = registers.cylinderLow;
            cdb[8] = registers.deviceHead;
            cdb[9] = registers.command;

            byte[] senseBuffer;
            int error = SendScsiCommand(fd, cdb, ref buffer, out senseBuffer, timeout, AtaProtocolToScsiDirection(protocol), out duration, out sense);

            // Now get error registers
            byte[] returnCdb = new byte[12];
            returnCdb[0] = (byte)Enums.ScsiCommands.AtaPassThrough;
            returnCdb[1] = (byte)(((byte)Enums.AtaProtocol.ReturnResponse << 1) & 0x1E);
            byte[] returnBuffer = new byte[14];
            bool returnSense;
            double returnDuration;

            SendScsiCommand(fd, returnCdb, ref returnBuffer, out senseBuffer, timeout, ScsiIoctlDirection.In, out returnDuration, out returnSense);
            if (returnBuffer[0] != 0x09 && returnBuffer[1] != 0x0C)
                return error;

            errorRegisters.error = returnBuffer[3];
            errorRegisters.sectorCount = returnBuffer[5];
            errorRegisters.sector = returnBuffer[7];
            errorRegisters.cylinderHigh = returnBuffer[9];
            errorRegisters.cylinderLow = returnBuffer[11];
            errorRegisters.deviceHead = returnBuffer[12];
            errorRegisters.status = returnBuffer[13];

            sense |= error != 0;

            return error;
        }

        internal static int SendAtaCommand(int fd, Structs.AtaRegistersLBA28 registers,
            out Structs.AtaErrorRegistersLBA28 errorRegisters, Enums.AtaProtocol protocol,
            Enums.AtaTransferRegister transferRegister, ref byte[] buffer, uint timeout,
            bool transferBlocks, out double duration, out bool sense)
        {
            duration = 0;
            sense = false;
            errorRegisters = new Structs.AtaErrorRegistersLBA28();

            if (buffer == null)
                return -1;

            byte[] cdb = new byte[12];
            cdb[0] = (byte)Enums.ScsiCommands.AtaPassThrough;
            cdb[1] = (byte)(((byte)protocol << 1) & 0x1E);
            if (transferRegister != Enums.AtaTransferRegister.NoTransfer &&
                protocol != Enums.AtaProtocol.NonData)
            {
                switch (protocol)
                {
                    case Enums.AtaProtocol.PioIn:
                    case Enums.AtaProtocol.UDmaIn:
                        cdb[2] = 0x08;
                        break;
                    default:
                        cdb[2] = 0x00;
                        break;
                }

                if (transferBlocks)
                    cdb[2] |= 0x04;

                cdb[2] |= (byte)((int)transferRegister & 0x03);
            }

            cdb[3] = registers.feature;
            cdb[4] = registers.sectorCount;
            cdb[5] = registers.lbaLow;
            cdb[6] = registers.lbaMid;
            cdb[7] = registers.lbaHigh;
            cdb[8] = registers.deviceHead;
            cdb[9] = registers.command;

            byte[] senseBuffer;
            int error = SendScsiCommand(fd, cdb, ref buffer, out senseBuffer, timeout, AtaProtocolToScsiDirection(protocol), out duration, out sense);

            // Now get error registers
            byte[] returnCdb = new byte[12];
            returnCdb[0] = (byte)Enums.ScsiCommands.AtaPassThrough;
            returnCdb[1] = (byte)(((byte)Enums.AtaProtocol.ReturnResponse << 1) & 0x1E);
            byte[] returnBuffer = new byte[14];
            bool returnSense;
            double returnDuration;

            SendScsiCommand(fd, returnCdb, ref returnBuffer, out senseBuffer, timeout, ScsiIoctlDirection.In, out returnDuration, out returnSense);
            if (returnBuffer[0] != 0x09 && returnBuffer[1] != 0x0C)
                return error;

            errorRegisters.error = returnBuffer[3];
            errorRegisters.sectorCount = returnBuffer[5];
            errorRegisters.lbaLow = returnBuffer[7];
            errorRegisters.lbaMid = returnBuffer[9];
            errorRegisters.lbaHigh = returnBuffer[11];
            errorRegisters.deviceHead = returnBuffer[12];
            errorRegisters.status = returnBuffer[13];

            sense |= error != 0;

            return error;
        }

        internal static int SendAtaCommand(int fd, Structs.AtaRegistersLBA48 registers,
            out Structs.AtaErrorRegistersLBA48 errorRegisters, Enums.AtaProtocol protocol,
            Enums.AtaTransferRegister transferRegister, ref byte[] buffer, uint timeout,
            bool transferBlocks, out double duration, out bool sense)
        {
            duration = 0;
            sense = false;
            errorRegisters = new Structs.AtaErrorRegistersLBA48();

            if (buffer == null)
                return -1;

            byte[] cdb = new byte[16];
            cdb[0] = (byte)Enums.ScsiCommands.AtaPassThrough16;
            cdb[1] |= 0x01;
            cdb[1] = (byte)(((byte)protocol << 1) & 0x1E);
            if (transferRegister != Enums.AtaTransferRegister.NoTransfer &&
                protocol != Enums.AtaProtocol.NonData)
            {
                switch (protocol)
                {
                    case Enums.AtaProtocol.PioIn:
                    case Enums.AtaProtocol.UDmaIn:
                        cdb[2] = 0x08;
                        break;
                    default:
                        cdb[2] = 0x00;
                        break;
                }

                if (transferBlocks)
                    cdb[2] |= 0x04;

                cdb[2] |= (byte)((int)transferRegister & 0x03);
            }

            cdb[3] = (byte)((registers.feature & 0xFF00) >> 8);
            cdb[4] = (byte)(registers.feature & 0xFF);
            cdb[5] = (byte)((registers.sectorCount & 0xFF00) >> 8);
            cdb[6] = (byte)(registers.sectorCount & 0xFF);
            cdb[7] = (byte)((registers.lbaLow & 0xFF00) >> 8);
            cdb[8] = (byte)(registers.lbaLow & 0xFF);
            cdb[9] = (byte)((registers.lbaMid & 0xFF00) >> 8);
            cdb[10] = (byte)(registers.lbaMid & 0xFF);
            cdb[11] = (byte)((registers.lbaHigh & 0xFF00) >> 8);
            cdb[12] = (byte)(registers.lbaHigh & 0xFF);
            cdb[13] = registers.deviceHead;
            cdb[14] = registers.command;

            byte[] senseBuffer;
            int error = SendScsiCommand(fd, cdb, ref buffer, out senseBuffer, timeout, AtaProtocolToScsiDirection(protocol), out duration, out sense);

            // Now get error registers
            byte[] returnCdb = new byte[16];
            returnCdb[0] = (byte)Enums.ScsiCommands.AtaPassThrough16;
            returnCdb[1] = (byte)(((byte)Enums.AtaProtocol.ReturnResponse << 1) & 0x1E);
            byte[] returnBuffer = new byte[14];
            bool returnSense;
            double returnDuration;

            SendScsiCommand(fd, returnCdb, ref returnBuffer, out senseBuffer, timeout, ScsiIoctlDirection.In, out returnDuration, out returnSense);
            if (returnBuffer[0] != 0x09 && returnBuffer[1] != 0x0C)
                return error;

            errorRegisters.error = returnBuffer[3];

            errorRegisters.sectorCount = (ushort)((returnBuffer[4] << 8) + returnBuffer[5]);
            errorRegisters.lbaLow = (ushort)((returnBuffer[6] << 8) + returnBuffer[7]);
            errorRegisters.lbaMid = (ushort)((returnBuffer[8] << 8) + returnBuffer[9]);
            errorRegisters.lbaHigh = (ushort)((returnBuffer[10] << 8) + returnBuffer[11]);
            errorRegisters.deviceHead = returnBuffer[12];
            errorRegisters.status = returnBuffer[13];

            sense |= error != 0;

            return error;
        }
    }
}
