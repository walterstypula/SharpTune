﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SharpTune;
using System.IO;

namespace SharpTune.ConversionTools
{
    public class Define
    {
        public string name { get; private set; }
        public string type { get; private set; }
        public Dictionary<int,IdaDef> idaDefs { get; private set; }
        public string offset {get; private set; }

        public Define(XElement xe)
        {
            idaDefs = new Dictionary<int, IdaDef>();
            name = xe.Attribute("name").Value.ToString();
            if (xe.Attribute("type") != null)
                type = xe.Attribute("type").Value.ToString();
            else
                type = null;
            IEnumerable<XElement> te = xe.Elements();
            idaDefs.Add(0,new IdaDef(name));
            foreach (var xi in xe.Elements())
            {
                IdaDef tid = new IdaDef(xi);
                idaDefs.Add(tid.priority, tid);
            }
        }

        public Define(string n, string hexaddr)
        {
            name = n;
            offset = hexaddr;
        }

        public string printType()
        {
            if (type == null)
                return null;
            return "(" + type + ")";
        }

        public void print(StreamWriter writer)
        {
            if (offset == null)
                Console.WriteLine("COULD NOT FIND DEFINE: " + name);
            else
                writer.WriteLine("#define " + name + " (" + this.printType() + "0x" + offset + ")");
        }

        public void findOffset(Dictionary<string, string> map)
        {
            foreach (var entry in map)
            {
                string tdef = null;
                for (int i = 0; i < idaDefs.Count; i++)
                {
                    if (entry.Key.EqualsCI(idaDefs[i].name))
                    {
                        offset = entry.Value.ToString();
                        return;
                    }
                }
            }
        }
    }
}
