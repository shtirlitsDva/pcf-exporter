﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using MoreLinq;
using System.Text;
using System.Data;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.ApplicationServices;
using xel = Microsoft.Office.Interop.Excel;

using PCF_Functions;
using Shared;
using Shared.BuildingCoder;
using PCF_Exporter;
using pdef = PCF_Functions.ParameterDefinition;
using plst = PCF_Functions.ParameterList;
using iv = PCF_Functions.InputVars;

namespace PCF_Parameters
{
    public class ExportParameters
    {
        public void ExportUndefinedElements(Document doc, string excelPath)
        {
            //Read existing values
            DataSet dataSetWithHeaders = Shared.DataHandler.ImportExcelToDataSet(excelPath, "YES");
            DataTable Elements = Shared.DataHandler.ReadDataTable(dataSetWithHeaders.Tables, "Elements");
            DataTable Pipelines = Shared.DataHandler.ReadDataTable(dataSetWithHeaders.Tables, "Pipelines");

            #region Pipelines
            //Collect all pipelines
            FilteredElementCollector colPL = new FilteredElementCollector(doc);
            var systems = colPL.OfCategory(BuiltInCategory.OST_PipingSystem).ToHashSet();
            var names = systems.DistinctBy(x => x.get_Parameter(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM).AsValueString())
                               .Select(x => x.get_Parameter(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM).AsValueString()).ToHashSet();

            //Instantiate excel
            xel.Application excel = new xel.Application();
            if (null == excel) throw new Exception("Failed to start EXCEL!");
            excel.Visible = true;
            xel.Workbook workbook = excel.Workbooks.Add(Missing.Value);
            xel.Worksheet worksheet;

            //Process pipelines
            worksheet = excel.ActiveSheet as xel.Worksheet;
            worksheet.Name = "MISSING_PIPELINES";
            worksheet.Columns.ColumnWidth = 20;

            //Compare values and write those who are not in configuration workbook
            int row = 1;
            int col = 1;
            foreach (string name in names)
            {
                //See if record already is defined
                if (Pipelines.AsEnumerable().Any(dataRow => dataRow.Field<string>(0) == name)) continue;
                worksheet.Cells[row, col] = name;
                row++;
            }
            #endregion

            #region Elements
            //Process elements
            excel.Sheets.Add(Missing.Value, Missing.Value, Missing.Value, Missing.Value);
            worksheet = excel.ActiveSheet as xel.Worksheet;
            worksheet.Name = "MISSING_ELEMENTS";
            worksheet.Columns.ColumnWidth = 20;

            //Collect all elements
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector = Shared.Filter.GetElementsWithConnectors(doc);
            HashSet<Element> elements = collector.ToElements().ToHashSet();
            HashSet<Element> limitedElements = (from Element e in elements
                                                where FilterDiameterLimit.FilterDL(e)
                                                select e).ToHashSet();
            HashSet<Element> filteredElements = (from Element e in limitedElements
                                                 where e.Category.Id.IntegerValue != (int)BuiltInCategory.OST_PipeCurves
                                                 select e).ToHashSet();

            IEnumerable<IGrouping<string, Element>> elementGroups =
                from e in filteredElements
                group e by e.get_Parameter(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM)
                .AsValueString();

            //Compare values and write those who are not in configuration workbook
            row = 1;
            col = 1;
            foreach (IGrouping<string, Element> gp in elementGroups)
            {
                //See if record already is defined
                if (Elements.AsEnumerable().Any(dataRow => dataRow.Field<string>(0) == gp.Key)) continue;
                worksheet.Cells[row, col] = gp.Key;
                row++;
            }
            #endregion
        }

        internal Result ExecuteMyCommand(UIApplication uiApp)
        {
            Document doc = uiApp.ActiveUIDocument.Document;

            FilteredElementCollector collector = new FilteredElementCollector(doc);

            #region Pipeline schedule export

            //Collect piping systems
            collector.OfClass(typeof(PipingSystem));

            //Group all elements by their Family and Type
            IOrderedEnumerable<Element> orderedCollector = collector.OrderBy(e => e.get_Parameter(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM).AsValueString());
            IEnumerable<IGrouping<string, Element>> elementGroups = from e in orderedCollector group e by e.get_Parameter(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM).AsValueString();

            xel.Application excel = new xel.Application();
            if (null == excel)
            {
                BuildingCoderUtilities.ErrorMsg("Failed to get or start Excel.");
                return Result.Failed;
            }
            excel.Visible = true;

            xel.Workbook workbook = excel.Workbooks.Add(Missing.Value);
            xel.Worksheet worksheet;
            worksheet = excel.ActiveSheet as xel.Worksheet;
            worksheet.Name = "PCF Export - pipelines";

            worksheet.Columns.ColumnWidth = 20;

            worksheet.Cells[1, 1] = "Family and Type";

            //Change domain for query
            string curDomain = "PIPL", curUsage = "U";

            var query = from p in new plst().LPAll
                        where p.Domain == curDomain && p.Usage == curUsage
                        select p;

            worksheet.Range["A1", BuildingCoderUtilities.GetColumnName(query.Count()) + "1"].Font.Bold = true;

            //Export family and type names to first column and parameter values
            int row = 2, col = 2;
            foreach (IGrouping<string, Element> gp in elementGroups)
            {
                worksheet.Cells[row, 1] = gp.Key;
                foreach (var p in query.ToList())
                {
                    if (row == 2) worksheet.Cells[1, col] = p.Name; //Fill out top row only in the first iteration
                    ElementId id = gp.First().GetTypeId();
                    PipingSystemType ps = (PipingSystemType)doc.GetElement(id); //SystemType parameters can only be read from type elements
                    worksheet.Cells[row, col] = ps.get_Parameter(p.Guid).AsString();
                    col++; //Increment column
                }
                row++; col = 2; //Increment row and reset column
            }

            #endregion

            #region Element schedule export

            //Define a collector (Pipe OR FamInst) AND (Fitting OR Accessory OR Pipe).
            //This is to eliminate FamilySymbols from collector which would throw an exception later on.
            collector = Shared.Filter.GetElementsWithConnectors(doc);

            //Group all elements by their Family and Type
            orderedCollector =
                collector.OrderBy(e => e.get_Parameter(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM).AsValueString());
            elementGroups = from e in orderedCollector
                            group e by e.get_Parameter(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM).AsValueString();


            excel.Sheets.Add(Missing.Value, Missing.Value, Missing.Value, Missing.Value);
            worksheet = excel.ActiveSheet as xel.Worksheet;
            worksheet.Name = "PCF Export - elements";

            worksheet.Columns.ColumnWidth = 20;

            worksheet.Cells[1, 1] = "Family and Type";

            //Query parameters
            curDomain = "ELEM";

            //Formatting must occur here, because it depends on query
            worksheet.Range["A1", BuildingCoderUtilities.GetColumnName(query.Count()) + "1"].Font.Bold = true;

            //Export family and type names to first column and parameter values
            row = 2; col = 2;
            foreach (IGrouping<string, Element> gp in elementGroups)
            {
                worksheet.Cells[row, 1] = gp.Key;
                foreach (var p in query)
                {
                    if (row == 2) worksheet.Cells[1, col] = p.Name; //Fill out top row only in the first iteration
                    worksheet.Cells[row, col] = gp.First().get_Parameter(p.Guid).AsString();
                    col++; //Increment column
                }
                row++; col = 2; //Increment row and reset column
            }

            #endregion



            collector.Dispose();
            return Result.Succeeded;
        }
    }

    public class PopulateParameters
    {
        internal Result PopulateElementData(UIApplication uiApp, ref string msg, DataTable dataTable)
        {
            List<string> ParameterNames = (from dc in dataTable.Columns.Cast<DataColumn>() select dc.ColumnName).ToList();
            ParameterNames.RemoveAt(0);
            //Test to see if the list of parameter names is defined at all, if not -- break.
            if (ParameterNames.IsNullOrEmpty())
            {
                BuildingCoderUtilities.ErrorMsg("Parameter names are incorrectly defined. Please reselect the EXCEL workbook.");
                return Result.Failed;
            };
            Document doc = uiApp.ActiveUIDocument.Document;
            //string filename = path;
            StringBuilder sbFeedback = new StringBuilder();

            //Failure feedback
            Element elementRefForFeedback = null;

            FilteredElementCollector collector = Shared.Filter.GetElementsWithConnectors(doc);

            //prepare input variables which are initialized when looping the elements
            string eFamilyType = null; string columnName = null;

            //query is using the variables in the loop to query the dataset
            EnumerableRowCollection<string> query = from value in dataTable.AsEnumerable()
                                                    where value.Field<string>(0) == eFamilyType
                                                    select value.Field<string>(columnName);

            var pQuery = from p in new plst().LPAll
                         where p.Domain == "ELEM"
                         select p;

            //Debugging
            //StringBuilder sbParameters = new StringBuilder();

            using (Transaction trans = new Transaction(doc, "Initialize PCF parameters"))
            {
                trans.Start();

                //Loop all elements pipes and fittings and accessories, setting parameters as defined in the dataset
                try
                {
                    //Reporting the number of different elements initialized
                    int pNumber = 0, fNumber = 0, aNumber = 0;
                    foreach (Element element in collector)
                    {
                        //Feedback
                        elementRefForFeedback = element;

                        //Filter out elements in ARGD (Rigids) system type
                        Cons cons = new Cons(element);
                        if (cons.Primary.MEPSystemAbbreviation(doc) == "ARGD") continue;

                        //reporting
                        if (string.Equals(element.Category.Name.ToString(), "Pipes")) pNumber++;
                        else if (string.Equals(element.Category.Name.ToString(), "Pipe Fittings")) fNumber++;
                        else if (string.Equals(element.Category.Name.ToString(), "Pipe Accessories")) aNumber++;

                        eFamilyType = element.get_Parameter(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM).AsValueString();
                        foreach (string parameterName in ParameterNames) // <-- ParameterNames must be correctly initialized!!!
                        {
                            columnName = parameterName; //This is needed to execute query correctly by deferred execution
                            string parameterValue = query.FirstOrDefault();
                            if (string.IsNullOrEmpty(parameterValue)) continue;
                            Guid parGuid = (from d in pQuery where d.Name == parameterName select d.Guid).First();
                            //Check if parGuid returns a match
                            if (parGuid == null)
                            {
                                BuildingCoderUtilities.ErrorMsg("Wrong parameter set. Select ELEMENT parameters.");
                                return Result.Failed;
                            }

                            //Writing the parameter data
                            //Implementing Overwrite or Append here
                            if (iv.Overwrite) element.get_Parameter(parGuid).Set(parameterValue);
                            else
                            {
                                Parameter par = element.get_Parameter(parGuid);
                                if (string.IsNullOrEmpty(par.ToValueString())) par.Set(parameterValue);
                                else continue;
                            }
                        }

                        //sbParameters.Append(eFamilyType);
                        //sbParameters.AppendLine();
                    }

                    //sbParameters.Append(eFamilyType);
                    //sbParameters.AppendLine();

                    
                    sbFeedback.Append(pNumber + " Pipes initialized.\n" + fNumber + " Pipe fittings initialized.\n" + aNumber + " Pipe accessories initialized.");
                    BuildingCoderUtilities.InfoMsg(sbFeedback.ToString());
                    //excelReader.Close();

                    //// Debugging
                    //// Clear the output file
                    //File.WriteAllBytes(InputVars.OutputDirectoryFilePath + "Parameters.pcf", new byte[0]);

                    //// Write to output file
                    //using (StreamWriter w = File.AppendText(InputVars.OutputDirectoryFilePath + "Parameters.pcf"))
                    //{
                    //    w.Write(sbParameters);
                    //    w.Close();
                    //}
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                catch (Exception ex)
                {
                    msg = ex.Message;
                    BuildingCoderUtilities.ErrorMsg($"Population of parameters failed with the following exception: \n" + msg +
                                                    $"\n For element {elementRefForFeedback.Id.IntegerValue}.");
                    trans.RollBack();
                    return Result.Failed;
                }

                trans.Commit();
            }

            return Result.Succeeded;

        }

        internal Result PopulatePipelineData(UIApplication uiApp, ref string msg, DataTable dataTable)
        {
            List<string> ParameterNames = (from dc in dataTable.Columns.Cast<DataColumn>() select dc.ColumnName).ToList();
            ParameterNames.RemoveAt(0);
            //Test to see if the list of parameter names is defined at all, if not -- break.
            if (ParameterNames.IsNullOrEmpty())
            {
                BuildingCoderUtilities.ErrorMsg("Parameter names are incorrectly defined. Please reselect the EXCEL workbook.");
                return Result.Failed;
            };
            Document doc = uiApp.ActiveUIDocument.Document;
            StringBuilder sbFeedback = new StringBuilder();

            //Get the systems of things and get the SystemTypes
            //Collector for PipingSystems
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            IList<Element> elementList = collector.OfClass(typeof(PipingSystem)).ToElements();
            //Collector returns Element, cast to PipingSystem
            IList<PipingSystem> systemList = elementList.Cast<PipingSystem>().ToList();
            //Get the PipingSystemType Id from the PipingSystem elements
            IList<ElementId> systemTypeIdList = systemList.Select(sys => sys.GetTypeId()).ToList();
            //Retrieve PipingSystemType from doc
            IEnumerable<Element> systemTypeList = from id in systemTypeIdList select doc.GetElement(id);
            //Group PipingSystemType by Name and retrieve first element of group -> equals to filtering a list to contain only unique elements
            List<Element> sQuery = (from st in systemTypeList
                                    group st by new { st.Name } //http://stackoverflow.com/a/9589705/6073998 {st.Name, st.Attribute1, st.Attribute2}
                into stGroup
                                    select stGroup.First()).ToList();

            //prepare input variables which are initialized when looping the elements
            string eFamilyType = null; string columnName = null;

            //query is using the variables in the loop to query the dataset
            EnumerableRowCollection<string> query = from value in PCF_Exporter_form.dataTableElements.AsEnumerable()
                                                    where value.Field<string>(0) == eFamilyType
                                                    select value.Field<string>(columnName);

            //Get a query for pipeline parameters
            var pQuery = from p in new plst().LPAll
                         where p.Domain == "PIPL"
                         select p;

            //Debugging
            //StringBuilder sbParameters = new StringBuilder();

            using (Transaction trans = new Transaction(doc, "Initialize PCF parameters"))
            {
                trans.Start();

                //Loop all elements pipes and fittings and accessories, setting parameters as defined in the dataset
                try
                {
                    //Reporting the number of different elements initialized
                    int sNumber = 0;
                    foreach (Element element in sQuery)
                    {
                        //reporting
                        sNumber++;

                        eFamilyType = "Piping System: " + element.Name;
                        foreach (string parameterName in pd.parameterNames) // <-- pd.parameterNames must be correctly initialized by FormCaller!!!
                        {
                            columnName = parameterName; //This is needed to execute query correctly by deferred execution
                            string parameterValue = query.FirstOrDefault();
                            if (string.IsNullOrEmpty(parameterValue)) continue;
                            Guid parGuid = (from d in pQuery.ToList() where d.Name == parameterName select d.Guid).First();
                            //Check if parGuid returns a match
                            if (parGuid == null)
                            {
                                BuildingCoderUtilities.ErrorMsg("Wrong parameter set. Select PIPELINE parameters.");
                                return Result.Failed;
                            }
                            element.get_Parameter(parGuid).Set(parameterValue);
                        }

                        //sbParameters.Append(eFamilyType);
                        //sbParameters.AppendLine();
                    }

                    //sbParameters.Append(eFamilyType);
                    //sbParameters.AppendLine();
                    //}
                    
                    sbFeedback.Append(sNumber + " Pipe Systems (Pipelines) initialized.\n");
                    BuildingCoderUtilities.InfoMsg(sbFeedback.ToString());
                    //excelReader.Close();

                    //// Debugging
                    //// Clear the output file
                    //File.WriteAllBytes(InputVars.OutputDirectoryFilePath + "Parameters.pcf", new byte[0]);

                    //// Write to output file
                    //using (StreamWriter w = File.AppendText(InputVars.OutputDirectoryFilePath + "Parameters.pcf"))
                    //{
                    //    w.Write(sbParameters);
                    //    w.Close();
                    //}
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
                catch (Exception ex)
                {
                    msg = ex.Message;
                    BuildingCoderUtilities.ErrorMsg("Population of parameters failed with the following exception: \n" + msg);
                    trans.RollBack();
                    return Result.Failed;
                }
                trans.Commit();
            }
            return Result.Succeeded;
        }


    }

    public class CreateParameterBindings
    {
        internal Result CreateElementBindings(UIApplication uiApp, ref string msg)
        {
            Document doc = uiApp.ActiveUIDocument.Document;
            Application app = doc.Application;
            Autodesk.Revit.Creation.Application ca = app.Create;

            Category pipeCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_PipeCurves);
            Category fittingCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_PipeFitting);
            Category accessoryCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_PipeAccessory);

            CategorySet catSet = ca.NewCategorySet();
            catSet.Insert(pipeCat);
            catSet.Insert(fittingCat);
            catSet.Insert(accessoryCat);

            string ExecutingAssemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string oriFile = app.SharedParametersFilename;
            string tempFile = ExecutingAssemblyPath + "Temp.txt";

            StringBuilder sbFeedback = new StringBuilder();
            //Parameter query
            var query = from p in new plst().LPAll
                        where p.Domain == "ELEM" ||
                              p.Name == "PCF_ELEM_EXCL"
                        select p;

            //Create parameter bindings
            try
            {
                Transaction trans = new Transaction(doc, "Bind element PCF parameters");
                trans.Start();
                foreach (pdef parameter in query.ToList())
                {
                    using (File.Create(tempFile)) { }
                    app.SharedParametersFilename = tempFile;
                    ExternalDefinitionCreationOptions options = new ExternalDefinitionCreationOptions(parameter.Name, parameter.Type)
                    {
                        GUID = parameter.Guid
                    };
                    ExternalDefinition def = app.OpenSharedParameterFile().Groups.Create("TemporaryDefinitionGroup").Definitions.Create(options) as ExternalDefinition;

                    BindingMap map = doc.ParameterBindings;
                    Binding binding = app.Create.NewInstanceBinding(catSet);

                    if (map.Contains(def)) sbFeedback.Append("Parameter " + parameter.Name + " already exists.\n");
                    else
                    {
                        map.Insert(def, binding, InputVars.PCF_BUILTIN_GROUP_NAME);
                        if (map.Contains(def)) sbFeedback.Append("Parameter " + parameter.Name + " added to project.\n");
                        else sbFeedback.Append("Creation of parameter " + parameter.Name + " failed for some reason.\n");
                    }
                    File.Delete(tempFile);
                }
                trans.Commit();
                BuildingCoderUtilities.InfoMsg(sbFeedback.ToString());
            }

            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            catch (Exception ex)
            {
                msg = ex.Message;
                return Result.Failed;
            }

            app.SharedParametersFilename = oriFile;

            return Result.Succeeded;

        }

        internal Result CreatePipelineBindings(UIApplication uiApp, ref string msg)
        {
            // UIApplication uiApp = commandData.Application;
            Document doc = uiApp.ActiveUIDocument.Document;
            Application app = doc.Application;
            Autodesk.Revit.Creation.Application ca = app.Create;

            Category pipelineCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_PipingSystem);

            CategorySet catSet = ca.NewCategorySet();
            catSet.Insert(pipelineCat);

            string ExecutingAssemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string oriFile = app.SharedParametersFilename;
            string tempFile = ExecutingAssemblyPath + "Temp.txt";

            StringBuilder sbFeedback = new StringBuilder();

            //Parameter query
            var query = from p in new plst().LPAll
                        where (p.Domain == "PIPL" || p.Name == "PCF_PIPL_EXCL") && p.ExportingTo != "LDT"
                        select p;

            //Create parameter bindings
            try
            {
                Transaction trans = new Transaction(doc, "Bind PCF parameters");
                trans.Start();
                foreach (pdef parameter in query.ToList())
                {
                    using (File.Create(tempFile)) { }
                    app.SharedParametersFilename = tempFile;
                    ExternalDefinitionCreationOptions options = new ExternalDefinitionCreationOptions(parameter.Name, parameter.Type)
                    {
                        GUID = parameter.Guid
                    };
                    ExternalDefinition def = app.OpenSharedParameterFile().Groups.Create("TemporaryDefinitionGroup").Definitions.Create(options) as ExternalDefinition;

                    BindingMap map = doc.ParameterBindings;
                    Binding binding = app.Create.NewTypeBinding(catSet);

                    if (map.Contains(def)) sbFeedback.Append("Parameter " + parameter.Name + " already exists.\n");
                    else
                    {
                        map.Insert(def, binding, InputVars.PCF_BUILTIN_GROUP_NAME);
                        if (map.Contains(def)) sbFeedback.Append("Parameter " + parameter.Name + " added to project.\n");
                        else sbFeedback.Append("Creation of parameter " + parameter.Name + " failed for some reason.\n");
                    }
                    File.Delete(tempFile);
                }
                trans.Commit();
                BuildingCoderUtilities.InfoMsg(sbFeedback.ToString());
            }

            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            catch (Exception ex)
            {
                msg = ex.Message;
                return Result.Failed;
            }

            app.SharedParametersFilename = oriFile;

            return Result.Succeeded;

        }
    }

    public class DeleteParameters
    {
        private StringBuilder sbFeedback = new StringBuilder();

        internal Result ExecuteMyCommand(UIApplication uiApp, ref string msg)
        {
            // UIApplication uiApp = commandData.Application;
            Document doc = uiApp.ActiveUIDocument.Document;

            //Call the method to delete parameters
            try
            {
                Transaction trans = new Transaction(doc, "Delete PCF parameters");
                trans.Start();
                foreach (pdef parameter in new plst().LPAll.ToList())
                    RemoveSharedParameterBinding(doc.Application, parameter.Name, parameter.Type);
                trans.Commit();
                BuildingCoderUtilities.InfoMsg(sbFeedback.ToString());
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            catch (Exception ex)
            {
                msg = ex.Message;
                return Result.Failed;
            }

            return Result.Succeeded;

        }

        //Method deletes parameters
        public void RemoveSharedParameterBinding(Application app, string name, ParameterType type)
        {
            BindingMap map = (new UIApplication(app)).ActiveUIDocument.Document.ParameterBindings;
            DefinitionBindingMapIterator it = map.ForwardIterator();
            it.Reset();

            Definition def = null;
            while (it.MoveNext())
            {
                if (it.Key != null && it.Key.Name == name && type == it.Key.ParameterType)
                {
                    def = it.Key;
                    break;
                }
            }

            if (def == null) sbFeedback.Append("Parameter " + name + " does not exist.\n");
            else
            {
                map.Remove(def);
                if (map.Contains(def)) sbFeedback.Append("Failed to delete parameter " + name + " for some reason.\n");
                else sbFeedback.Append("Parameter " + name + " deleted.\n");
            }
        }
    }
}