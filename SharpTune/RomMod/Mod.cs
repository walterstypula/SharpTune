﻿/*
    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SharpTune;
using System.Windows.Forms;
using System.Text.RegularExpressions;

namespace SharpTune.RomMod
{
    /// <summary>
    /// Defines and Applies a Mod (series of patches) to a ROM.
    /// </summary>
    public class Mod
    {
        public const uint BaselineOffset = 0xFF000000;
        private const uint metadataAddress = 0x80001000;
        private const uint requiredVersionPrefix = 0x12340000;
        private const uint calibrationIdPrefix = 0x12340001;
        private const uint patchPrefix = 0x12340002;
        private const uint copyPatchPrefix = 0x12340012;
        private const uint newPatchPrefix = 0x12340004;
        private const uint copyNewPatchPrefix = 0x12340014;
        private const uint replace4BytesPrefix = 0x12340003;
        private const uint replaceLast2Of4BytesPrefix = 0x12340013;
        private const uint modNamePrefix = 0x12340007;
        private const uint modVersionPrefix = 0x12340009;
        private const uint modAuthorPrefix = 0x12340008;
        public const uint endoffile = 0x00090009;
        private const uint jsrhookPrefix = 0x1234000A;
        private const uint ecuIdPrefix = 0x1234000B;
        private const uint newEcuIdPrefix = 0x1234000C;
        private const uint modInfoPrefix = 0x1234000D;
        private const uint modIdPrefix = 0x1234000F;
        private const string delim = "\0\0\0\0\0";

        public long FileSize { get; private set; }
        public string FileName { get; private set; }
        public string FilePath { get; private set; }

        public string CalId { get; private set; }//todo get rid of this, only need init/final
        public uint CalIdAddress { get; private set; }
        public uint CalIdLength { get; private set; }

        public bool isApplied { get; set; }
            //IDEA: set this based on calid match to activeimage
        public bool isResource { get; private set; }
        public bool isCompat { get; private set; }
        public string info { get; private set; }
        public Stream modStream { get; private set; }
        public string direction
        {
            get
            {
                if (isApplied)
                    return "Remove";
                else
                    return "Apply";
            }
            private set { }
        }
        public string ModVersion { get; private set; }
        public string ModAuthor { get; private set; }
        public string ModName { get; private set; }
        public string ModInfo { get; private set; }

        public uint ModIdentAddress { get; private set; }
        public string ModIdent { get; private set; }

        public BlobList blobList { get; set; }
        public string InitialCalibrationId { get; private set; }
        public string FinalCalibrationId { get; private set; }
        private readonly SRecordReader reader;
        public uint EcuIdAddress { get; private set; }
        public uint EcuIdLength { get; private set; }
        public string InitialEcuId { get; private set; }
        public string FinalEcuId { get; private set; }
        public List<Patch> patchList;
        public List<Patch> unPatchList;
        public ModDefinition modDef { get; private set; }
        public string buildConfig;

        /// <summary>
        /// Constructor for external mods
        /// </summary>
        /// <param name="modPath"></param>
        public Mod(string modPath)
        {
            this.patchList = new List<Patch>();
            this.ModAuthor = "Unknown Author";
            this.ModName = "Unknown Mod";
            this.ModVersion = "Unknown Version";
            //MemoryStream modMemStream = new MemoryStream();
            //using (FileStream fileStream = File.OpenRead(modPath))
            //{
            //    modMemStream.SetLength(fileStream.Length);
            //    fileStream.Read(modMemStream.GetBuffer(), 0, (int)fileStream.Length);
            //}
            FileInfo f = new FileInfo(modPath);
            FileSize = f.Length;
            FileName = f.Name;
            FilePath = modPath;
            isResource = false;
            reader = new SRecordReader(modPath);//modMemStream, modPath);
            TryReadPatches();
            TryReversePatches();
        }

        public Mod(string modPath, string bc)
        {
            this.buildConfig = bc;
            this.patchList = new List<Patch>();
            this.ModAuthor = "Unknown Author";
            this.ModName = "Unknown Mod";
            this.ModVersion = "Unknown Version";
            //MemoryStream modMemStream = new MemoryStream();
            //using (FileStream fileStream = File.OpenRead(modPath))
            //{
            //    modMemStream.SetLength(fileStream.Length);
            //    fileStream.Read(modMemStream.GetBuffer(), 0, (int)fileStream.Length);
            //}
            FileInfo f = new FileInfo(modPath);
            FileSize = f.Length;
            FileName = f.Name;
            FilePath = modPath;
            isResource = false;
            reader = new SRecordReader(modPath);//modMemStream, modPath);
            TryReadPatches();
            TryReversePatches();
        }

        /// <summary>
        /// Constructor for embedded mods
        /// </summary>
        /// <param name="s"></param>
        /// <param name="modPath"></param>
        public Mod(Stream s, string modPath)
        {
            this.patchList = new List<Patch>();
            this.ModAuthor = "Unknown Author";
            this.ModName = "Unknown Mod";
            this.ModVersion = "Unknown Version";
            reader = new SRecordReader(s, modPath);
            FileName = modPath;
            isResource = true;
            modStream = s;
            TryReadPatches();
            TryReversePatches();
        }
        
        public bool TryDefinition(string defPath)
        {
            this.modDef = new ModDefinition(this);
            if (!modDef.TryReadDefs(defPath)) return false;

            if (buildConfig != null)
                defPath = defPath + "/MerpMod/" + buildConfig + "/";
            defPath += ModIdent.ToString() + ".xml";

            modDef.definition.ExportXML(defPath);
            return true;
        }

        public bool TryCheckApplyMod(string romPath, string outPath, bool apply, bool commit)
        {
            if (patchList == null || patchList.Count == 0)
                return false;
            ///string workingPath = outPath + ".temp";
            //File.Copy(romPath, outPath, true);
            //File.Copy(romPath, workingPath, true);//File.Open(workingPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            MemoryStream outStream = new MemoryStream();
            using (FileStream fileStream = File.OpenRead(romPath))
            {
                outStream.SetLength(fileStream.Length);
                fileStream.Read(outStream.GetBuffer(), 0, (int)fileStream.Length);
            }

            Console.WriteLine("This patch file was intended for: {0}.", this.InitialCalibrationId);
            Console.WriteLine("This patch file converts ROM to:  {0}.", this.ModIdent);
            Console.WriteLine("This mod was created by: {0}.", this.ModAuthor);
            Console.WriteLine("Mod Information: {0} Version: {1}.", this.ModName, this.ModVersion);

            if (apply && TryValidatePatches(outStream))
            {
                isApplied = false;
                isCompat = true;
                Console.WriteLine("This patch file was NOT previously applied to this ROM file.");
            }
            else if (!apply && TryValidateUnPatches(outStream))
            {
                isApplied = true;
                isCompat = true;
                Console.WriteLine("This patch file was previously applied to this ROM file.");
            }
            else
                isCompat = false;

            if (!isCompat)
            {
                Console.WriteLine(this.FileName + " is mod is NOT compatible with this ROM file.");
                return false;
            }
            if (!commit)
            {
                Console.WriteLine(this.FileName + " is compatible with this ROM file.");
                return true;
            }
            if (isApplied)
            {
                Console.WriteLine("Removing patch.");
                if (this.TryRemoveMod(outStream))
                {
                    Console.WriteLine("Verifying patch removal.");
                    using (Verifier verifier = new Verifier(outStream, reader, !isApplied))
                    {
                        if (!verifier.TryVerify(this.patchList))
                        {
                            Console.WriteLine("Verification failed, ROM file not modified.");
                            return false;
                        }
                    }
                    //File.Copy(workingPath, outPath, true);
                    //File.Delete(workingPath);
                    try
                    {
                        using (FileStream fileStream = File.OpenWrite(outPath))
                        {
                            outStream.Seek(0, SeekOrigin.Begin);
                            outStream.CopyTo(fileStream);
                        }
                        Console.WriteLine("ROM file modified successfully, Mod has been removed. Successfully saved image to {0}", outPath);
                    }
                    catch (System.Exception excpt)
                    {
                        MessageBox.Show("Error accessing file! It is locked!", "RomMod", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Console.WriteLine("Error accessing file! It is locked!");
                        Console.WriteLine(excpt.Message);
                        return false;
                    }
                    return true;
                }
                else
                {
                    Console.WriteLine("The ROM file has not been modified.");
                    return false;
                }
            }
            else
            {
                Console.WriteLine("Applying mod.");
                if (this.TryApplyMod(outStream))
                {
                    Console.WriteLine("Verifying mod.");
                    using (Verifier verifier = new Verifier(outStream, reader, !isApplied))
                    {
                        if (!verifier.TryVerify(this.patchList))
                        {
                            Console.WriteLine("Verification failed, ROM file not modified.");
                            return false;
                        }
                        try
                        {
                            using (FileStream fileStream = File.OpenWrite(outPath))
                            {
                                outStream.Seek(0, SeekOrigin.Begin);
                                outStream.CopyTo(fileStream);
                            }
                            Console.WriteLine("ROM file modified successfully, Mod has been applied. Successfully saved image to {0}", outPath);
                        }
                        catch (System.Exception excpt)
                        {
                            MessageBox.Show("Error accessing file! It is locked!", "RomMod", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            Console.WriteLine("Error accessing file! It is locked!");
                            Console.WriteLine(excpt.Message);
                            return false;
                        }
                        outStream.Dispose();
                        //File.Copy(workingPath, outPath, true);
                        //File.Delete(workingPath);
                        //TODO CHECK outstream disposal!!!
                        Console.WriteLine("ROM file modified successfully, mod has been applied.");
                        return true;
                    }
                }
                else
                {
                    Console.WriteLine("The ROM file has not been modified.");
                    return false;
                }
            }
        }

        /// <summary>
        /// Create the patch start/end metadata from the patch file.
        /// </summary>
        public bool TryReadPatches()
        {
            List<Blob> blobs = new List<Blob>();

            BlobList bloblist;
            if (!this.TryReadBlobs(out bloblist))
            {
                return false;
            }
            blobs = bloblist.Blobs;
            this.blobList = bloblist;
            Blob metadataBlob;
            if (!this.TryGetMetaBlob(metadataAddress, 10, out metadataBlob, blobs))
            {
                Console.WriteLine("This patch file does not contain metadata.");
                return false;
            }

            if (!this.TryReadMetadata(metadataBlob, blobs))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Print patch descriptions to the console.
        /// </summary>
        public void PrintPatches()
        {
            foreach (Patch patch in this.patchList)
            {
                if (patch.StartAddress > BaselineOffset)
                {
                    continue;
                }

                Console.WriteLine(patch.ToString());
            }
        }



        /// <summary>
        /// Reverses the "direction" of the patch by start address manipulation
        /// </summary>
        /// <returns></returns>
        /// 
        public bool TryReversePatches()
        {
            using (var stream = new System.IO.MemoryStream())
            {
                var binaryFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                binaryFormatter.Serialize(stream, patchList); //serialize to stream
                stream.Position = 0;
                //deserialize from stream.
                unPatchList = binaryFormatter.Deserialize(stream) as List<Patch>;
            }
            //Console.ReadKey();
            foreach (Patch patch in this.unPatchList)
            {


                //Swap contents
                Blob tempblob;

                if (patch.IsNewPatch)
                {
                    //set all bytes in baseline blob to 0xFF
                    for (int i = 0; i < patch.Payload.Content.Count; i++)
                    {
                        patch.Payload.Content[i] = 0xFF;       
                    }

                }
                else
                {
                    tempblob = patch.Baseline.CloneWithNewStartAddress(patch.Baseline.StartAddress - BaselineOffset);
                    patch.Baseline = patch.Payload.CloneWithNewStartAddress(patch.Payload.StartAddress + BaselineOffset);
                    patch.Payload.Content.Clear();
                    patch.Payload = tempblob;
                }


                //OLD CODE
                //new payload
                //baselineBlob = baselineBlob.CloneWithNewStartAddress(baselineBlob.StartAddress - BaselineOffset);

                //new baseline
                //modifiedBlob = modifiedBlob.CloneWithNewStartAddress(modifiedBlob.StartAddress + BaselineOffset);

                //first blob in list is the patch payload, second is baseline
                //newBlobs.Add(baselineBlob);
                //newBlobs.Add(modifiedBlob);
            }

            return true;
        }

        /// <summary>
        /// Determine whether the data that the patch was designed to overwrite match what's actually in the ROM.
        /// </summary>
        public bool TryValidatePatches(Stream romStream)
        {
            Console.WriteLine("Attempting to validate patches...");
            bool allPatchesValid = true;
            foreach (Patch patch in this.patchList)
            {
                Console.Write(patch.ToString() + " - ");

                if (patch.GetType() == typeof(PullJSRHookPatch))
                {
                    if (!this.ValidateJSRHookBytes((PullJSRHookPatch)patch, romStream))
                    {
                        // Pass/fail message is printed by ValidateBytes().
                        allPatchesValid = false;
                    }
                }
                else
                {

                    if (!this.ValidateBytes(patch, romStream))
                    {
                        // Pass/fail message is printed by ValidateBytes().
                        allPatchesValid = false;
                    }
                }
            }

            if (!allPatchesValid)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determine whether the data that the patch was designed to overwrite match what's actually in the ROM.
        /// </summary>
        public bool TryValidateUnPatches(Stream romStream)
        {
            Console.WriteLine("Attempting to validate patch removal...");
            bool allPatchesValid = true;
            foreach (Patch patch in this.unPatchList)
            {
                Console.Write(patch.ToString() + " - ");

                if (patch.IsNewPatch)
                {
                    Console.WriteLine("DATA SECTION WILL BE OVERWRITTEN");
                    continue;
                }

                if (!this.ValidateBytes(patch, romStream))
                {
                    // Pass/fail message is printed by ValidateBytes().
                    allPatchesValid = false;
                }
            }

            if (!allPatchesValid)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Try to apply the patches to the ROM.
        /// </summary>
        public bool TryApplyMod(Stream romStream)
        {
            foreach (Patch patch in this.patchList)
            {
                if (!TryApplyPatch(patch, romStream))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Try to remove patches from a ROM
        /// </summary>
        /// <returns></returns>
        public bool TryRemoveMod(Stream romStream)
        {
            foreach (Patch patch in this.unPatchList)
            {
                if (!this.TryApplyPatch(patch,romStream))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Extract actual ROM data, for appending to a patch file.
        /// </summary>
        public bool TryPrintBaselines(string patchPath, Stream romStream)
        {
            string p = this.ModIdent.ToString() + ".patch";
            File.Copy(patchPath, p , true);

            bool result = true;
            foreach (Patch patch in this.patchList)
            {
                if (!this.TryCheckPrintBaseline(patch,romStream))
                {
                    result = false;
                    Console.WriteLine("ERROR OCCURRED DURING BASELINE PRINT, POSSIBLE MISMATCH BETWEEN METADATA AND ROM!");
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Try to read blobs from the patch file.
        /// </summary>
        private bool TryReadBlobs(out BlobList blist)
        {
            BlobList list = new BlobList();
            this.reader.Open();
            SRecord record;
            while (this.reader.TryReadNextRecord(out record))
            {
                if (!record.IsValid)
                {
                    Console.WriteLine("The patch file contains garbage - was it corrupted somehow?");
                    Console.WriteLine("Line {0}: {1}", record.LineNumber, record.RawData);
                    blist = null;
                    return false;
                }

                list.ProcessRecord(record);
                
            }

            blist = list;
            return true;
        }

        /// <summary>
        /// Try to read the patch file metadata (start and end addresses of each patch, etc).
        /// </summary>
        private bool TryReadMetadata(Blob blob, List<Blob> blobs)
        {
            int offset = 0;
            
            if (!TryConfirmPatchVersion(blob, ref offset))
            {
                return false;
            }

            if (!TryReadMetaHeader8(blob, ref offset))
            {
                return false;
            }

            offset = 0;

            if (!this.TryReadPatches(blob, ref offset, blobs))
            {
                return false;
            }

            if (this.patchList.Count < 3)
            {
                Console.WriteLine("This patch file contains no patches.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Try to read the 'required version' metadata.
        /// </summary>
        private bool TryConfirmPatchVersion(Blob blob, ref int offset)
        {
            uint tempUInt32 = 0;

            if (!blob.TryGetUInt32(ref tempUInt32, ref offset))
            {
                Console.WriteLine("This patch file's metadata is way too short (no version metadata).");
                return false;
            }

            if (tempUInt32 != requiredVersionPrefix)
            {
                Console.WriteLine("This patch file's metadata starts with {0}, it should start with {1}", tempUInt32, requiredVersionPrefix);
                return false;
            }

            if (!blob.TryGetUInt32(ref tempUInt32, ref offset))
            {
                Console.WriteLine("This patch file's metadata is way too short (no version).");
                return false;
            }

            if (tempUInt32 != RomMod.Version)
            {
                Console.WriteLine("This is RomPatch.exe version {0}.", RomMod.Version);
                Console.WriteLine("This patch file requires version {0}.", tempUInt32);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Try to read the initial and final calibration IDs
        /// </summary>
        private bool TryReadCalibrationChange(Blob blob, ref int offset)
        {
            uint tempUInt32 = 0;

            if (!blob.TryGetUInt32(ref tempUInt32, ref offset))
            {
                Console.WriteLine("This patch file's metadata is way too short (no calibration metadata).");
                return false;
            }

            if (tempUInt32 != calibrationIdPrefix)
            {
                Console.WriteLine("Expected calibration id prefix {0:X8}, found {1:X8}", calibrationIdPrefix, tempUInt32);
                return false;
            }

            if (!blob.TryGetUInt32(ref tempUInt32, ref offset))
            {
                Console.WriteLine("This patch file's metadata is way too short (no calibration address).");
                return false;
            }

            uint calibrationAddress = tempUInt32;

            if (!blob.TryGetUInt32(ref tempUInt32, ref offset))
            {
                Console.WriteLine("This patch file's metadata is way too short (no calibration length).");
                return false;
            }

            uint calibrationLength = tempUInt32;

            string initialCalibrationId;
            if (!this.TryReadCalibrationId(blob, ref offset, out initialCalibrationId))
            {
                return false;
            }

            this.InitialCalibrationId = initialCalibrationId;

            string finalCalibrationId;
            if (!this.TryReadCalibrationId(blob, ref offset, out finalCalibrationId))
            {
                return false;
            }

            this.FinalCalibrationId = finalCalibrationId;

            // Synthesize calibration-change patch and blobs.
            Patch patch = new Patch(
                calibrationAddress, 
                calibrationAddress + (calibrationLength - 1));

            patch.IsMetaChecked = true;

            patch.Baseline = new Blob(
                calibrationAddress + Mod.BaselineOffset,
                Encoding.ASCII.GetBytes(initialCalibrationId));
                
            patch.Payload = new Blob(
                calibrationAddress, 
                Encoding.ASCII.GetBytes(finalCalibrationId));
            
            this.patchList.Add(patch);
            
            return true;
        }

        /// <summary>
        /// Try to read the calibration ID from the patch metadata.
        /// </summary>
        private bool TryReadCalibrationId(Blob blob, ref int offset, out string calibrationId)
        {
            calibrationId = string.Empty;
            List<byte> calibrationIdBytes = new List<byte>();

            byte tempByte = 0;
            for (int index = 0; index < 16; index++)
            {
                if (!blob.TryGetByte(ref tempByte, ref offset))
                {
                    Console.WriteLine("This patch file's metadata ran out before the complete calibration ID could be found.");
                    return false;
                }

                if (calibrationId == string.Empty)
                {
                    if (tempByte != 0)
                    {
                        calibrationIdBytes.Add(tempByte);
                    }
                    else
                    {
                        calibrationId = System.Text.Encoding.ASCII.GetString(calibrationIdBytes.ToArray());
                    }
                }
                else
                {
                    if (tempByte != 0)
                    {
                        Console.WriteLine("This patch file's metadata contains garbage after the calibration ID.");
                        return false;
                    }
                }
            }

            return true;
        }

        private bool TryReadMetaHeader8(Blob metadata, ref int offset)
        {
            UInt32 cookie = 0;
            uint tempInt = 0;
            Patch patch = null;
            while ((metadata.Content.Count > offset + 8) &&
                metadata.TryGetUInt32(ref cookie, ref offset))
            {
                if (cookie == Mod.calibrationIdPrefix)
                {
                    if (!metadata.TryGetUInt32(ref tempInt, ref offset))
                    {
                        Console.WriteLine("This patch file's metadata is way too short (no calibration address).");
                        return false;
                    }

                    this.CalIdAddress = tempInt;

                    if (!metadata.TryGetUInt32(ref tempInt, ref offset))
                    {
                        Console.WriteLine("This patch file's metadata is way too short (no calibration length).");
                        return false;
                    }

                    this.CalIdLength = tempInt;

                    string initialCalibrationId;
                    if (!this.TryReadCalibrationId(metadata, ref offset, out initialCalibrationId))
                    {
                        return false;
                    }

                    this.InitialCalibrationId = initialCalibrationId;

                    string finalCalibrationId;
                    if (!this.TryReadCalibrationId(metadata, ref offset, out finalCalibrationId))
                    {
                        return false;
                    }

                    if (finalCalibrationId.ContainsCI("ffffffff"))
                    {
                        StringBuilder s = new StringBuilder(initialCalibrationId, 0,initialCalibrationId.Length, initialCalibrationId.Length);
                        s.Remove(initialCalibrationId.Length-2,2);
                        s.Insert(initialCalibrationId.Length-2,"MM");
                        FinalCalibrationId = s.ToString();
                    }

                    this.FinalCalibrationId = finalCalibrationId;
                    // Synthesize calibration-change patch and blobs.
                    patch = new Patch(
                        CalIdAddress,
                        CalIdAddress + (CalIdLength - 1));

                    patch.IsMetaChecked = true;

                    patch.Baseline = new Blob(
                        CalIdAddress + Mod.BaselineOffset,
                        Encoding.ASCII.GetBytes(initialCalibrationId));

                    patch.Payload = new Blob(
                        CalIdAddress,
                        Encoding.ASCII.GetBytes(finalCalibrationId));

                    this.patchList.Add(patch);
                }
                else if (cookie == modIdPrefix)
                {
                    if (metadata.TryGetUInt32(ref tempInt, ref offset))
                    {
                        this.ModIdentAddress = tempInt;
                    }
                    string metaString = null;
                    if (this.TryReadMetaString(metadata, out metaString, ref offset))
                    {
                        // found modName, output to string!
                        this.ModIdent = metaString;
                    }
                }
                else if (cookie == ecuIdPrefix)
                {
                    if (metadata.TryGetUInt32(ref tempInt, ref offset))
                    {
                        this.EcuIdAddress = tempInt;
                    }
                    if (metadata.TryGetUInt32(ref tempInt, ref offset))
                    {
                        this.EcuIdLength = tempInt;
                    }
                    string metaString = null;
                    if (this.TryReadMetaString(metadata, out metaString, ref offset))
                    {
                        // found modName, output to string!
                        this.InitialEcuId = metaString;
                    }
                    metadata.TryGetUInt32(ref tempInt, ref offset);
                    if (this.TryReadMetaString(metadata, out metaString, ref offset))
                    {
                        // found modName, output to string!
                        this.FinalEcuId = metaString;
                    }
                }
                else if (cookie == modNamePrefix)
                {
                    string metaString = null;
                    if (this.TryReadMetaString(metadata, out metaString, ref offset))
                    {
                        // found modName, output to string!
                        this.ModName = metaString;
                    }
                    else
                    {
                        Console.WriteLine("Invalid ModName found." + patch.ToString());
                        return false;
                    }
                }
                else if (cookie == modAuthorPrefix)
                {
                    string metaString = null;
                    if (this.TryReadMetaString(metadata, out metaString, ref offset))
                    {
                        // found modName, output to string!
                        this.ModAuthor = metaString;
                    }
                    else
                    {
                        Console.WriteLine("Invalid patch found." + patch.ToString());
                        return false;
                    }
                }
                else if (cookie == modVersionPrefix)
                {
                    string metaString = null;
                    if (this.TryReadMetaString(metadata, out metaString, ref offset))
                    {
                        // found modName, output to string!
                        this.ModVersion = metaString;
                    }
                    else
                    {
                        Console.WriteLine("Invalid patch found." + patch.ToString());
                        return false;
                    }
                }
                else if (cookie == modInfoPrefix)
                {
                    string metaString = null;
                    if (this.TryReadMetaString(metadata, out metaString, ref offset))
                    {
                        // found modinfo, output to string!
                        this.ModInfo = metaString;
                    }
                    else
                    {
                        Console.WriteLine("Invalid modInfo found." + patch.ToString());
                        return false;
                    }
                }
                else if (cookie == endoffile)
                {
                    break;
                }
            }
            if (this.InitialEcuId.Length == this.FinalEcuId.Length)
            {
                // Synthesize calibration-change patch and blobs.
                if (FinalEcuId.ContainsCI("ffffffff"))
                {
                    StringBuilder feid = new StringBuilder(Regex.Split(this.ModIdent,"_v")[1].Replace(".", ""));
                    while (feid.Length < this.InitialEcuId.Length)
                    {
                        feid.Append("F");
                    }
                    this.FinalEcuId = feid.ToString();
                }
                patch = new Patch(
                    EcuIdAddress,
                    EcuIdAddress + ((EcuIdLength / 2) - 1));

                patch.IsMetaChecked = true;

                patch.Baseline = new Blob(
                    EcuIdAddress + Mod.BaselineOffset,
                    InitialEcuId.ToByteArray());

                patch.Payload = new Blob(
                    EcuIdAddress,
                    FinalEcuId.ToByteArray());

                this.patchList.Add(patch);
            }
            if (this.patchList.Count < 2)
            {
                Console.WriteLine("This patch file's metadata contains no CALID or ECUID patch!!.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Try to read the Patch metadata from the file.
        /// </summary>
        private bool TryReadPatches(Blob metadata, ref int offset, List<Blob> blobs)
        {
            UInt32 cookie = 0;
            while ((metadata.Content.Count > offset + 8) &&
                metadata.TryGetUInt32(ref cookie, ref offset))
            {
                Patch patch = null;
                if (cookie == patchPrefix)
                {
                    if (!this.TryReadPatch(metadata, out patch, ref offset, blobs))
                    {
                        Console.WriteLine("Invalid patch found." + patch.ToString());
                        return false;
                    }
                }
                else if (cookie == copyPatchPrefix)
                {
                    if (!this.TryReadCopyPatch(metadata, out patch, ref offset, blobs))
                    {
                        Console.WriteLine("Invalid patch found." + patch.ToString());
                        return false;
                    }
                }
                else if (cookie == replace4BytesPrefix)
                {
                    if (!this.TrySynthesize4BytePatch(metadata, out patch, ref offset))
                    {
                        Console.WriteLine("Invalid 4-byte patch found." + patch.ToString());
                        return false;
                    }

                }
                else if (cookie == replaceLast2Of4BytesPrefix)
                {
                    if (!this.TrySynthesizeLast2Of4BytePatch(metadata, out patch, ref offset))
                    {
                        Console.WriteLine("Invalid Last 2 Of 4-byte patch found." + patch.ToString());
                        return false;
                    }

                }
                else if (cookie == newPatchPrefix)
                {
                    if (!this.TryReadPatch(metadata, out patch, ref offset, blobs))
                    {
                        Console.WriteLine("Invalid patch found." + patch.ToString());
                        return false;
                    }

                    patch.IsNewPatch = true;
                }
                else if (cookie == copyNewPatchPrefix)
                {
                    if (!this.TryReadCopyPatch(metadata, out patch, ref offset, blobs))
                    {
                        Console.WriteLine("Invalid patch found." + patch.ToString());
                        return false;
                    }

                    patch.IsNewPatch = true;
                }
                else if (cookie == jsrhookPrefix)
                {
                    if (!this.TrySynthesizePullJsrHookPatch(metadata, out patch, ref offset, blobs))
                    {
                        Console.WriteLine("Invalid patch found." + patch.ToString());
                        return false;
                    }
                }
                else if (cookie == endoffile)
                {
                    break;
                }

                //if (patch == null && !isInfo)
                //{
                //    throw new Exception("Internal error in TryReadPatches: Patch is null. cookie: " + cookie.ToString()
                //        + "offset: " + offset.ToString());
                //}
                if (patch != null)
                {
                    this.patchList.Add(patch);
                }
            }
            return true;
        }


        /// <summary>
        /// Read a single string from the metadata blob.
        /// </summary>
        /// <remarks>
        /// Consider returning false, printing error message.  But, need to 
        /// be certain to abort the whole process at that point...
        /// </remarks>
        public bool TryReadMetaString(Blob metadata, out string metaString, ref int offset)
        {
            metaString = null;
            UInt32 cookie = 0;
            List<byte> tempbytelist = new List<byte>();

             while ((metadata.Content.Count > offset + 8) &&
                 metadata.TryGetUInt32(ref cookie, ref offset))
             {
                 if (cookie < 0x12340010 && cookie > 12340000)
                 {
                     offset -= 4;

                     char[] splitter = { '\0' }; //TODO FIX THIS
                     string tempstring = System.Text.Encoding.ASCII.GetString(tempbytelist.ToArray());
                     metaString = tempstring.Split(splitter)[0];
                     return true;
                 }

                 byte tempbyte = new byte();
                  offset -= 4;
                 for (int i = 0; i < 4; i++)
                 {
                     if (!metadata.TryGetByte(ref tempbyte,ref offset))
                     {
                         return false;
                     }
                     tempbytelist.Add(tempbyte);
                     
                 }

             }

            tempbytelist.ToString();
            return false;
        }


        /// <summary>
        /// Read a single Patch from the metadata blob.
        /// </summary>
        /// <remarks>
        /// Consider returning false, printing error message.  But, need to 
        /// be certain to abort the whole process at that point...
        /// </remarks>
        private bool TryReadPatch(Blob metadata, out Patch patch, ref int offset, List<Blob> blobs )
        {
            uint start = 0;
            uint end = 0;
                        
            if (!metadata.TryGetUInt32(ref start, ref offset))
            {
                throw new InvalidDataException("This patch's metadata contains an incomplete patch record (no start address).");
            }
                        
            if (!metadata.TryGetUInt32(ref end, ref offset))
            {
                throw new InvalidDataException("This patch's metadata contains an incomplete patch record (no end address).");
            }

             
            patch = new Patch(start, end);

            Blob baselineBlob;

            

            if (!this.TryGetPatchBlob(patch.StartAddress + BaselineOffset, patch.Length, out baselineBlob, blobs))
            {
                if (baselineBlob != null)  return false;
            }

                patch.Baseline = baselineBlob;
            

            Blob payloadBlob;
            if (!this.TryGetPatchBlob(patch.StartAddress, patch.Length, out payloadBlob, blobs))
            {
                return false;
            }

            patch.Payload = payloadBlob;
            return true;
        }

        /// <summary>
        /// Read a single Offset Patch from the metadata blob.
        /// Offset patch reads from Srecord "copystart"
        /// Writes to rom at "start"
        /// </summary>
        /// <remarks>
        /// Consider returning false, printing error message.  But, need to 
        /// be certain to abort the whole process at that point...
        /// </remarks>
        private bool TryReadCopyPatch(Blob metadata, out Patch patch, ref int offset, List<Blob> blobs)
        {
            uint copystart = 0;
            uint start = 0;
            uint end = 0;

            if (!metadata.TryGetUInt32(ref copystart, ref offset))
            {
                throw new InvalidDataException("This patch's metadata contains an incomplete patch record (no copystart address).");
            }

            if (!metadata.TryGetUInt32(ref start, ref offset))
            {
                throw new InvalidDataException("This patch's metadata contains an incomplete patch record (no start address).");
            }

            if (!metadata.TryGetUInt32(ref end, ref offset))
            {
                throw new InvalidDataException("This patch's metadata contains an incomplete patch record (no end address).");
            }


            patch = new Patch(start, end);

            patch.CopyStartAddress = copystart;

            Blob baselineBlob;



            if (!this.TryGetPatchBlob(patch.StartAddress + BaselineOffset, patch.Length, out baselineBlob, blobs))
            {
                if (baselineBlob != null) return false;
            }

            patch.Baseline = baselineBlob;


            Blob payloadBlob;

            //Read from the copystartaddress (HEW address space)
            if (!this.TryGetPatchBlob(patch.CopyStartAddress, patch.Length, out payloadBlob, blobs))
            {
                return false;
            }

            patch.Payload = payloadBlob;
            return true;
        }

        /// <summary>
        /// Construct a Pull JSR HOOK patch from the metadata blob.
        /// </summary>
        private bool TrySynthesizePullJsrHookPatch(Blob metadata, out Patch patch, ref int offset, List<Blob> blobs)
        {
            uint address = 0;

            if (!metadata.TryGetUInt32(ref address, ref offset))
            {
                throw new InvalidDataException("This patch's metadata contains an incomplete 4-byte patch record (no address).");
            }

            byte[] jsrbytes = new byte[] {0xF0,0x48,0x00,0x09};



            patch = new PullJSRHookPatch(address, address + 3);

            patch.IsMetaChecked = true;//remove thsi

            patch.Payload = new Blob(patch.StartAddress, jsrbytes);

            //this.blobs.Add(new Blob(address, BitConverter.GetBytes(newValue).Reverse()));
            //this.blobs.Add(new Blob(address + BaselineOffset, BitConverter.GetBytes(oldValue).Reverse()));
            return true;
        }

        /// <summary>
        /// Construct a 4-byte patch from the metadata blob.
        /// </summary>
        private bool TrySynthesize4BytePatch(Blob metadata, out Patch patch, ref int offset)
        {
            uint address = 0;
            uint oldValue = 0;
            uint newValue = 0;

            if (!metadata.TryGetUInt32(ref address, ref offset))
            {
                throw new InvalidDataException("This patch's metadata contains an incomplete 4-byte patch record (no address).");
            }

            if (!metadata.TryGetUInt32(ref oldValue, ref offset))
            {
                throw new InvalidDataException("This patch's metadata contains an incomplete 4-byte patch record (no baseline value).");
            }

            if (!metadata.TryGetUInt32(ref newValue, ref offset))
            {
                throw new InvalidDataException("This patch's metadata contains an incomplete 4-byte patch record (no patch value).");
            }

            patch = new Patch(address, address + 3);
            patch.IsMetaChecked = true;
            patch.Baseline = new Blob(address + BaselineOffset, BitConverter.GetBytes(oldValue).Reverse());
            patch.Payload = new Blob(address, BitConverter.GetBytes(newValue).Reverse());
            
            //this.blobs.Add(new Blob(address, BitConverter.GetBytes(newValue).Reverse()));
            //this.blobs.Add(new Blob(address + BaselineOffset, BitConverter.GetBytes(oldValue).Reverse()));
            return true;
        }

        /// <summary>
        /// Construct a Last 2 of 4-byte patch from the metadata blob.
        /// </summary>
        private bool TrySynthesizeLast2Of4BytePatch(Blob metadata, out Patch patch, ref int offset)
        {
            uint address = 0;
            uint oldValue = 0;
            uint newValue = 0;

            if (!metadata.TryGetUInt32(ref address, ref offset))
            {
                throw new InvalidDataException("This patch's metadata contains an incomplete l2o4-byte patch record (no address).");
            }

            if (!metadata.TryGetUInt32(ref oldValue, ref offset))
            {
                throw new InvalidDataException("This patch's metadata contains an incomplete l2o4-byte patch record (no baseline value).");
            }

            if (!metadata.TryGetUInt32(ref newValue, ref offset))
            {
                throw new InvalidDataException("This patch's metadata contains an incomplete l2o4-byte patch record (no patch value).");
            }

            patch = new Patch(address + 2, address + 3);
            patch.IsMetaChecked = true;
            patch.Baseline = new Blob(address + 2 + BaselineOffset, BitConverter.GetBytes(oldValue).Take(2).Reverse());
            patch.Payload = new Blob(address + 2, BitConverter.GetBytes(newValue).Take(2).Reverse());

            return true;
        }

        /// <summary>
        /// Print the current contents of the ROM in the address range for the given patch.
        /// Contains a check against metadata when IsMetaChecked = true
        /// </summary>
        private bool TryCheckPrintBaseline(Patch patch, Stream outStream)
        {
            uint patchLength = patch.Length;
            byte[] buffer = new byte[patchLength];

            //read baseline ROM data blob into buffer
            if (!this.TryReadBuffer(outStream, patch.StartAddress, buffer))
            {
                return false;
            }

           if (patch.IsMetaChecked)
            {
                patch.MetaCheck((IEnumerable<byte>)buffer);
            }

            using ( StreamWriter textWriter = File.AppendText(this.ModIdent + ".patch"))
            {
                //TextWriter textWriter = new StreamWriter(consoleOutputStream);
                

                //OUTPUT DIRECT TO FILE, ADD VERSIONING SYSTEM
               
                SRecordWriter writer = new SRecordWriter(textWriter);

                // The "baselineOffset" delta is how we distinguish baseline data from patch data.
                
                writer.Write(patch.StartAddress + BaselineOffset, buffer);
            }

            return true;
        }

        /// <summary>
        /// Try to read an arbitrary byte range from the ROM.
        /// </summary>
        private bool TryReadBuffer(Stream romStream, uint startAddress, byte[] buffer)
        {
            romStream.Seek(startAddress, SeekOrigin.Begin);
            long totalBytesRead = 0;
            long totalBytesToRead = buffer.Length;

            while (totalBytesRead < totalBytesToRead)
            {
                long bytesToRead = totalBytesToRead - totalBytesRead;
                int bytesRead = romStream.Read(
                    buffer,
                    (int) totalBytesRead,
                    (int) bytesToRead);

                if (bytesRead == 0)
                {
                    Console.WriteLine(
                        "Unable to read {0} bytes starting at position {1:X8}",
                        bytesToRead,
                        startAddress + totalBytesRead);
                    return false;
                }

                totalBytesRead += bytesRead;
            }

            return true;
        }

        /// <summary>
        /// Determine whether the bytes from a Patch's expected data match the contents of the ROM.
        /// </summary>
        private bool ValidateBytes(Patch patch, Stream romStream)
        {
            uint patchLength = patch.Length;
            byte[] buffer = new byte[patchLength];
            if (!this.TryReadBuffer(romStream, patch.StartAddress, buffer))
            {
                Console.Write("tryreadbuffer failed in validatebytes");
                return false;
            }

            //            DumpBuffer("Actual  ", buffer, buffer.Length);
            //            DumpBuffer("Expected", expectedData.Content, buffer.Length);

            int mismatches = 0;
            byte expected;

            for (int index = 0; index < patchLength; index++)
            {
                byte actual = buffer[index];

                if (!patch.IsNewPatch)
                {
                    if (index >= patch.Baseline.Content.Count)
                    {
                        Console.WriteLine("Expected data is smaller than patch size.");
                        return false;
                    }

                    expected = patch.Baseline.Content[index];
                }
                else
                    expected = 0xFF;

                if (actual != expected)
                {
                    mismatches++;
                }
            }

            if (mismatches == 0)
            {
                Console.WriteLine("Valid.");
                return true;
            }

            Console.WriteLine("Invalid.");
            Console.WriteLine("{0} bytes (of {1}) do not meet expectations.", mismatches, patchLength);
            return false;
        }

        /// <summary>
        /// Determine whether the bytes from a Patch's expected data match the contents of the ROM.
        /// </summary>
        private bool ValidateJSRHookBytes(PullJSRHookPatch patch, Stream romStream)
        {
            uint patchLength = patch.Length;
            byte[] buffer = new byte[patchLength];
            if (!this.TryReadBuffer(romStream, patch.StartAddress, buffer))
            {
                Console.Write("tryreadbuffer failed in validatebytes");
                return false;
            }

            //            DumpBuffer("Actual  ", buffer, buffer.Length);
            //            DumpBuffer("Expected", expectedData.Content, buffer.Length);

            //int mismatches = 0;

            for (int index = 0; index < patchLength; index++)
            {
                byte actual = buffer[index];

                if (!patch.MetaCheck((IEnumerable<byte>)buffer))
                {
                    Console.WriteLine("JSR HOOK FAILED");
                    return false;
                }
            }
            Console.WriteLine("Valid.");
            return true;
        }


        /// <summary>
        /// Try to find a blob in the patch that starts at the given address.
        /// </summary>
        public bool TryGetMetaBlob(uint startAddress, uint length, out Blob match, List<Blob> blobs)
        {
            foreach (Blob blob in blobs)
            {
                bool startsInsideBlob = blob.StartAddress <= startAddress;
                if (!startsInsideBlob)
                {
                    continue;
                }

                uint blobEndAddress = (uint)(blob.StartAddress + blob.Content.Count);
                bool endsInsideBlob = startAddress + length <= blobEndAddress;
                if (!endsInsideBlob)
                {
                    continue;
                }

                match = blob;
                return true;
            }

            match = null;
            return false;
        }


        /// <summary>
        /// Try to find a blob that starts at the given address.
        /// </summary>
        private bool TryGetPatchBlob(uint startAddress, uint length, out Blob match, List<Blob> blobs)
        {
            foreach (Blob blob in blobs)
            {
                

                uint blobEndAddress = (uint)(blob.StartAddress + blob.Content.Count);

                //Is start address we are searching for AFTER this blobs starting address?
                bool startsAfterBlobStart = startAddress >= blob.StartAddress;

               


                //This code has issues with signed/unsigned ints!
                //START
                //
                //if (startOffset < 0)
                //{
                //    continue;
                //}
                //END

                 //AND Is the end address we are searching for BEFORE the blobs end address?
                bool endsInsideBlob = startAddress + length <= blobEndAddress;
                
                if (!startsAfterBlobStart || !endsInsideBlob)
                {
                    continue;
                }
                uint startOffset = startAddress - blob.StartAddress;

                byte[] temp = new byte[length];

                blob.Content.CopyTo((int)startOffset, temp, 0, (int)length);

                match = new Blob(startAddress, temp);
                
                return true;
            }

            match = null;
            return false;
        }

        /// <summary>
        /// Given a patch, look up the content Blob and write it into the ROM.
        /// </summary>
        private bool TryApplyPatch(Patch patch, Stream romStream)
        {
            romStream.Seek(patch.StartAddress, SeekOrigin.Begin);
       
            if (patch.Payload == null)
            {
                Console.WriteLine("No blob found for patch starting at {0:X8}", patch.StartAddress);
                return false;
            }

            if (patch.StartAddress + patch.Payload.Content.Count != patch.EndAddress + 1)
                
            {
                Console.WriteLine("Payload blob for patch starting at {0:X8} does not contain the entire patch.", patch.StartAddress);
                Console.WriteLine("Patch start {0:X8}, end {1:X8}, length {2:X8}", patch.StartAddress, patch.EndAddress, patch.Length);
                Console.WriteLine("Payload blob length {2:X8}", patch.Payload.Content.Count);
                return false;
            }

            romStream.Write(patch.Payload.Content.ToArray(), 0, (int) patch.Payload.Content.Count);
            return true;
        }

    }
}
