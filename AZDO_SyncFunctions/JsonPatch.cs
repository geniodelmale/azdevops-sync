using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AZDO_SyncFunctions
{
    public class JsonPatch
    {
        string jsonContent = "[]";

        public override string ToString()
        {
            return jsonContent;
        }

        public StringContent StringContent()
        {
            return new StringContent(jsonContent, Encoding.UTF8, "application/json-patch+json");
        }
        public void Add(string op, string path, string from, string value)
        {
            string patch = "{";
            patch = AddField(patch, "op", op);
            patch = AddField(patch, "path", path);
            patch = AddField(patch, "from", from);
            patch = AddField(patch, "value", Utility.JSONEncode(value));
            patch += "}";

            if (patch != "{}")
            {
                if (jsonContent == "[]")
                    jsonContent = jsonContent.Replace("]", patch + "]");
                else
                    jsonContent = jsonContent.Replace("]", "," + patch + "]");
            }
        }

        public void AddCollection(string op, string path, string from, string value)
        {
            string patch = "{";
            patch = AddField(patch, "op", op);
            patch = AddField(patch, "path", path);
            patch = AddField(patch, "from", from);
            patch = AddFieldWithoutQuotes(patch, "value", value);
            patch += "}";

            if (patch != "{}")
            {
                if (jsonContent == "[]")
                    jsonContent = jsonContent.Replace("]", patch + "]");
                else
                    jsonContent = jsonContent.Replace("]", "," + patch + "]");
            }
        }

        private static string AddField(string patch, string cmd, string value)
        {

            if (value != null && value != "")
            {
                if (patch == "{")
                    return patch + "\"" + cmd + "\": \"" + value + "\"";
                else
                    return patch + ", \"" + cmd + "\": \"" + value + "\"";
            }

            return patch;
        }

        private static string AddFieldWithoutQuotes(string patch, string cmd, string value)
        {

            if (value != null && value != "")
            {
                if (patch == "{")
                    return patch + "\"" + cmd + "\": " + value;
                else
                    return patch + ", \"" + cmd + "\": " + value;
            }

            return patch;
        }

        public void ParseAndAddProperty(JProperty property)
        {
            //log.Info(property.Name + " - " + property.Value);

            switch (property.Name)
            {
                case "System.WorkItemType": // Ignore these fields
                case "System.TeamProject":
                case "System.CreatedDate":
                case "System.CreatedBy":
                case "System.ChangedDate":
                case "System.AuthorizedDate":
                case "System.RevisedDate":
                case "System.Watermark":
                case "System.ChangedBy":
                case "Microsoft.VSTS.Common.StateChangeDate":
                case "Microsoft.VSTS.Common.ActivatedDate":
                case "Microsoft.VSTS.Common.ActivatedBy":
                case "System.Rev":
                    break;
                case "System.AreaPath":     // Switch AreaPath and Iteration to match destination Team Project
                case "System.IterationPath":
                    Add("add", "/fields/" + property.Name, null, Utility.ProcessAreasAndIterations(property.Value.ToString()));
                    break;
                default:
                    Add("add", "/fields/" + property.Name, null, property.Value.ToString());
                    break;
            }
        }

        public void ParseAndAddProperty(string name, string value)
        {
            //log.Info(property.Name + " - " + property.Value);

            switch (name)
            {
                case "System.WorkItemType": // Ignore these fields
                case "System.TeamProject":
                case "System.CreatedDate":
                case "System.CreatedBy":
                case "System.ChangedDate":
                case "System.ChangedBy":
                case "Microsoft.VSTS.Common.StateChangeDate":
                case "Microsoft.VSTS.Common.ActivatedDate":
                case "Microsoft.VSTS.Common.ActivatedBy":
                case "System.Rev":
                    break;
                case "System.AreaPath":     // Switch AreaPath and Iteration to match destination Team Project
                case "System.IterationPath":
                    Add("add", "/fields/" + name, null, Utility.ProcessAreasAndIterations(value));
                    break;
                default:
                    Add("add", "/fields/" + name, null, value);
                    break;
            }
        }
    }
}
