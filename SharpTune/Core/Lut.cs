﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SharpTune.RomMod;
using System.IO;

namespace SharpTune.Core
{
    public static class LutFactory
    {
        public static Lut CreateLut(string name, uint address, Stream stream)
        {
            byte[] tba = new byte[32];
            stream.Read(tba,0,32);
            Blob tb = new Blob(address,tba);
            Lut3D lut3d;
            if (Lut3D.TrySynthesizeLut3D(out lut3d,name,tb,address))
                return lut3d;
            Lut2D lut2d;
            if (Lut2D.TrySynthesizeLut2D(out lut2d,name,tb,address))
                return new Lut2D(name,tb, address);
            else
                return new Lut(name,address);
        }
    }

    public class Lut
    {
        public readonly UInt16 rowColLimit = 35;
        public readonly UInt32 romLimit = 0x00018000;
        public readonly UInt32 ramStart = 0xFFFF0000;
        public readonly UInt32 ramLimit = 0x0000FFFF;
        public readonly List<uint> tableTypes = new List<uint>() { 0x08000000, 0x04000000 };
        //TODO: pull this info from each device image?

        

        public string Name;
        public uint dataAddress;

        

        //TODO: FUCKING POLYMORPHISM

        public Lut()
        {    
        }

        public Lut(string name, uint da)
        {
            Name = name;
            dataAddress = da;
        }
    }

    public class Lut2D : Lut
    {
        public byte[] rawdata;
        public int length;
        public UInt16 cols;

        public uint colsAddress;
        //for lookup tables
        public Lut2D()
        {
        }

        /// <summary>
        /// WARNING ONLY USE FOR 3d TABLES
        /// TODO arrange classes as 2D/3D and base
        /// </summary>
        /// <param name="blob"></param>
        /// <param name="address"></param>

        public Lut2D(string name,Blob blob, uint address)
        {
            Name = name;
            int addr = (int)(address - (uint)blob.StartAddress);
            blob.TryGetUInt16(ref cols, ref addr);
            addr += 2;
            blob.TryGetUInt32(ref colsAddress, ref addr);
            blob.TryGetUInt32(ref dataAddress, ref addr);
        }
        
        public static bool TrySynthesizeLut2D(out Lut2D lut, string name, Blob blob, uint addr)
        {
            lut = new Lut2D(name, blob, addr);
            if(lut.CheckData())
                return true;
            return false;
        }


        public virtual bool CheckData()
        {
            if( cols > rowColLimit &&
                ( (colsAddress > 0 && colsAddress < romLimit) || (colsAddress > ramStart && colsAddress < ramLimit) ) &&
                ( (dataAddress > 0 && dataAddress < romLimit) || (dataAddress > ramStart && dataAddress < ramLimit) ) )
                return true;
            return false;
        }
    }
        public class Lut3D : Lut2D
    {
        public UInt16 rows;
        public uint tableType;
        public uint rowsAddress;
        public uint gradient;
        public uint offset;
        
        //for lookup tables
        public Lut3D()
        {
        }

        /// <summary>
        /// WARNING ONLY USE FOR 3d TABLES
        /// TODO arrange classes as 2D/3D and base
        /// </summary>
        /// <param name="blob"></param>
        /// <param name="address"></param>

        public Lut3D(string name, Blob blob, uint address)
        {
            Name = name;
            int addr = (int)(address - (uint)blob.StartAddress);
            blob.TryGetUInt16(ref cols, ref addr);
            blob.TryGetUInt16(ref rows, ref addr);
            blob.TryGetUInt32(ref colsAddress, ref addr);
            blob.TryGetUInt32(ref rowsAddress, ref addr);
            blob.TryGetUInt32(ref dataAddress, ref addr);
            blob.TryGetUInt32(ref tableType, ref addr);
            blob.TryGetUInt32(ref gradient, ref addr);
            blob.TryGetUInt32(ref offset, ref addr);
        }

        public static bool TrySynthesizeLut3D(out Lut3D lut,string name,Blob blob, uint addr)
        {
            lut = new Lut3D(name,blob, addr);
            if(lut.CheckData())
                return true;
            return false;
        }

        public override bool CheckData()
        {
            if ( CheckType(tableType) &&
                base.CheckData() &&
                ( (rowsAddress > 0 && rowsAddress < romLimit) || (rowsAddress > ramStart && rowsAddress < ramLimit) ) &&
                CheckFloat(gradient) && CheckFloat(offset) )
                return true;
            return false;
        }

        public bool CheckType(uint t)
        {
            foreach(uint tt in tableTypes)
            {
                if(t == tt)
                    return true;
            }
            return false;
        }

        public bool CheckFloat(uint f)
        {
            //TODO actually check them
            return true;
        }
    }
}
