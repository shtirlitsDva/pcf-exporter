﻿ using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using NTR_Functions;
using dw = NTR_Functions.DataWriter;

namespace NTR_Exporter
{
    class NTR_Accessories
    {
        public static StringBuilder Export(string key, HashSet<Element> elements, ConfigurationData conf, Document doc)
        {
            var sbAccessories = new StringBuilder();

            foreach (Element element in elements)
            {
                //Read the family and type of the element
                string fat = element.get_Parameter(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM).AsValueString();
                
                //Read element kind
                string kind = dw.ReadElementTypeFromDataTable(fat, conf.Elements, "KIND");
                if (string.IsNullOrEmpty(kind)) kind = dw.ReadElementTypeFromDataTable(fat, conf.Supports, "KIND");
                if (string.IsNullOrEmpty(kind)) kind = dw.ReadElementTypeFromDataTable(fat, conf.Flexjoints, "KIND");
                if (string.IsNullOrEmpty(kind)) continue;

                //Write element kind
                sbAccessories.Append(kind);

                //Get the connectors
                var cons = Shared.MepUtils.GetConnectors(element);

                switch (kind)
                {
                    case "ARM":
                        sbAccessories.Append(dw.PointCoords("P1", cons.Primary));
                        sbAccessories.Append(dw.PointCoords("P2", cons.Secondary));
                        sbAccessories.Append(dw.PointCoords("PM", element));
                        sbAccessories.Append(dw.DnWriter("DN1", cons.Primary));
                        sbAccessories.Append(dw.DnWriter("DN2", cons.Secondary));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Elements, "GEW"));
                        break;
                    // Old SymbolicSupport cases
                    //case "SH":
                    //case "FH":
                    //    sbAccessories.Append(dw.PointCoords("PNAME", element));
                    //    sbAccessories.Append(dw.ReadParameterFromDataTable(fat, conf.Supports, "L"));
                    //    sbAccessories.Append(dw.WriteElementId(element, "REF"));
                    //    sbAccessories.AppendLine();
                    //    continue;
                    case "SH":
                    case "FH":
                        sbAccessories.Append(dw.PointCoords("PNAME", element));
                        if (kind == "FH") sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Supports, "CW"));
                        sbAccessories.Append(dw.HangerLength("L", element));
                        if (kind == "FH") sbAccessories.Append(dw.ParameterValue("RF", "NTR_ELEM_RF", element)); //Installation load -- calculate beforehand
                        sbAccessories.Append(dw.ParameterValue("TEXT", new[] { "TAG 1", "TAG 2" }, element));
                        sbAccessories.Append(dw.WriteElementId(element, "REF"));
                        sbAccessories.AppendLine();
                        continue;
                    case "FP":
                    case "AX":
                        sbAccessories.Append(dw.PointCoords("PNAME", element));
                        sbAccessories.Append(dw.ParameterValue("TEXT", new[] { "TAG 1", "TAG 2" }, element));
                        sbAccessories.Append(dw.WriteElementId(element, "REF"));
                        sbAccessories.AppendLine();
                        continue;
                    case "GL":
                        sbAccessories.Append(dw.PointCoords("PNAME", element));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Supports, "SAV"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Supports, "SAB"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Supports, "MAQ"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Supports, "MAV"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Supports, "SQV"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Supports, "SQB"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Supports, "MQA"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Supports, "MQV"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Supports, "SVV"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Supports, "SVB"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Supports, "MVA"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Supports, "MVQ"));
                        sbAccessories.Append(dw.ParameterValue("TEXT", new[] { "TAG 1", "TAG 2" }, element));
                        sbAccessories.Append(dw.WriteElementId(element, "REF"));
                        sbAccessories.AppendLine();
                        continue;
                    case "FL":
                    case "FGL":
                    case "FFL":
                    case "QS":
                    case "QSVX":
                        sbAccessories.Append(dw.PointCoords("PNAME", element));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Supports, "MALL"));
                        sbAccessories.Append(dw.ParameterValue("TEXT", new[] { "TAG 1", "TAG 2" }, element));
                        sbAccessories.Append(dw.WriteElementId(element, "REF"));
                        sbAccessories.AppendLine();
                        continue;
                    case "RO":
                        //Added for preinsulated district heating pipes in Pipe Accessory category
                        sbAccessories.Append(dw.PointCoords("P1", cons.Primary));
                        sbAccessories.Append(dw.PointCoords("P2", cons.Secondary));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Elements, "DN"));
                        break;
                    //Flexible joints hereafter
                    case "KLAT": //Lateral kompensator
                        sbAccessories.Append(dw.PointCoords("P1", cons.Primary));
                        sbAccessories.Append(dw.PointCoords("P2", cons.Secondary));
                        sbAccessories.Append(dw.DnWriter("DN", cons.Primary));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Flexjoints, "GEW"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Flexjoints, "CR"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Flexjoints, "CL"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Flexjoints, "CP"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Flexjoints, "CT"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Flexjoints, "L"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Flexjoints, "LMAX"));
                        sbAccessories.Append(dw.ReadPropertyFromDataTable(fat, conf.Flexjoints, "ANZRI"));
                        break;
                }

                sbAccessories.Append(dw.ReadPropertyFromDataTable(key, conf.Pipelines, "MAT")); //Is not required for FLABL?
                sbAccessories.Append(dw.ReadPropertyFromDataTable(key, conf.Pipelines, "LAST")); //Is not required for FLABL?
                sbAccessories.Append(dw.ParameterValue("TEXT", new[] { "TAG 1", "TAG 2" }, element));
                sbAccessories.Append(dw.WriteElementId(element, "REF"));
                sbAccessories.Append(" LTG=" + key);
                sbAccessories.AppendLine();

            }

            return sbAccessories;
        }
    }
}
