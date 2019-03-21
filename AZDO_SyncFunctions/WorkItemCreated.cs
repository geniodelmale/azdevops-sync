using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

namespace AZDO_SyncFunctions
{
    public static class WorkItemCreated
    {
        [FunctionName("WorkItemCreated")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            // original workitem ID is saved on destination workitem as a Tag "OriginalID=xxx;"
            // http links and attachments are working
            // links to git/tfvc and other kind of links require work to run correctly

            // some fields are ignored by default, like created by, date, updated by, date, etc...
            
            // If the endpoint is "Tested" with the test button of Azure DevOps Service Hook wizard
            // it gives an error because it receives "Fabrikam" as the team project
            // Don't know if it's ok to trap it or leave the error.

            log.Info($"++++++++++WorkItemCreate was triggered!");
            HttpContent requestContent = req.Content;
            string jsonContent = requestContent.ReadAsStringAsync().Result;
            log.Info(jsonContent);

            JObject originalWI = JObject.Parse(jsonContent);
            string workItemType = Uri.UnescapeDataString((string)originalWI["resource"]["fields"]["System.WorkItemType"]);
            string title = Uri.UnescapeDataString((string)originalWI["resource"]["fields"]["System.Title"]);
            string workItemID = (string)originalWI["resource"]["id"];

            string areapath = Utility.ProcessAreasAndIterations((string)originalWI["resource"]["fields"]["System.AreaPath"]);
            string iteration = Utility.ProcessAreasAndIterations((string)originalWI["resource"]["fields"]["System.IterationPath"]);

            string sourceVSTS = Uri.UnescapeDataString((string)originalWI["resourceContainers"]["collection"]["baseUrl"]);
            if (sourceVSTS.LastIndexOf('/') == sourceVSTS.Length - 1)
            {
                sourceVSTS = sourceVSTS.Substring(0, sourceVSTS.Length - 1);
            }
            string sourceTeamProject = Uri.UnescapeDataString((string)originalWI["resource"]["fields"]["System.TeamProject"]);

            log.Info(workItemType);
            log.Info(title);
            log.Info(workItemID);
            log.Info(sourceTeamProject);

            string destVSTS;
            string destPrj;
            string destPAT;

            Utility.SetVSTSAccountInfo("", sourceTeamProject, out destVSTS, out destPrj, out destPAT);

            log.Info("PAT . " + destPAT);

            if (workItemType == "Test Plan")
            {
                string testplan = await Utility.CreateTestPlan(destVSTS, destPAT, destPrj, workItemID, title, areapath, iteration, log);
                log.Info("Created testplan: " + testplan);
                return req.CreateResponse(HttpStatusCode.OK, "");
            }
            else if (workItemType == "Test Suite")
            {
                if (originalWI["resource"]["fields"]["Microsoft.VSTS.TCM.TestSuiteAudit"] == null)
                // first test suite in a test plan is automatically created when a test plan is created
                // we shouldn't create it, we should search for it and associate it
                {
                    string testPlan = await Utility.GetTestPlanFromRootTestSuite(sourceVSTS, sourceTeamProject, destPAT, workItemID, log);
                    string destTestPlan = await Utility.GetDestinationWorkItemId(destVSTS, destPAT, destPrj, testPlan, log);

                    string destSuites = await Utility.GetTestSuitesFromTestPlan(destVSTS, destPrj, destPAT, destTestPlan, log);
                    string id = GetRootTestSuite(destSuites);

                    if (id != null && await Utility.UpdateWorkItemOriginalIDTag(destVSTS, destPAT, destPrj, id, workItemID, log))
                    {
                        return req.CreateResponse(HttpStatusCode.OK, "");
                    }
                    else
                    {
                        return Utility.CreateErrorResponse(req, "Cannot update destination Test Suite:" + id);
                    }
                }
                else if (originalWI["resource"]["fields"]["Microsoft.VSTS.TCM.TestSuiteAudit"].ToString().StartsWith("Added test suite to parent:"))
                // other test suites inside a test plan should be created and associated normally
                {
                    string sourceParentTestSuite = originalWI["resource"]["fields"]["Microsoft.VSTS.TCM.TestSuiteAudit"].ToString();
                    sourceParentTestSuite = sourceParentTestSuite.Substring(sourceParentTestSuite.LastIndexOf(' ') + 1);
                    string sourceTestPlan = await Utility.GetTestPlanFromGenericTestSuite(sourceVSTS, sourceTeamProject, destPAT, sourceParentTestSuite, log);

                    string destParentTestSuite = await Utility.GetDestinationWorkItemId(destVSTS, destPAT, destPrj, sourceParentTestSuite, log);
                    string destTestPlan = await Utility.GetDestinationWorkItemId(destVSTS, destPAT, destPrj, sourceTestPlan, log);

                    string json = "";

                    if (originalWI["resource"]["fields"]["Microsoft.VSTS.TCM.TestSuiteType"].ToString() == "Static")
                    {
                        json = "{ \"suiteType\": \"StaticTestSuite\", \"name\": \"" + title + "\" }";
                    }
                    else if (originalWI["resource"]["fields"]["Microsoft.VSTS.TCM.TestSuiteType"].ToString() == "Query Based")
                    {
                        string queryText = originalWI["resource"]["fields"]["Microsoft.VSTS.TCM.QueryText"].ToString();
                        if (queryText.Contains("System.AreaPath") || queryText.Contains("System.IterationPath"))
                        {
                            queryText = Utility.ProcessAreasAndIterations(queryText);
                        }
                        json = "{ \"suiteType\": \"DynamicTestSuite\", \"name\": \"" + title + "\", " +
                            "\"queryString\": \"" + queryText + "\" }";
                    }
                    else if (originalWI["resource"]["fields"]["Microsoft.VSTS.TCM.TestSuiteType"].ToString() == "Requirement Based")
                    {
                        string sourceRequirementId = await Utility.GetRequirementId(sourceVSTS, sourceTeamProject, destPAT, sourceTestPlan, workItemID, log);
                        string destRequirementId = await Utility.GetDestinationWorkItemId(destVSTS, destPAT, destPrj, sourceRequirementId, log);

                        json = "{ \"suiteType\": \"RequirementTestSuite\", \"name\": \"" + title + "\", " +
                            "\"requirementIds\": [" + destRequirementId + "] }";
                    }

                    string testsuiteid = await Utility.CreateTestSuite(destVSTS, destPAT, destPrj, destTestPlan, destParentTestSuite, workItemID, json, log);
                    log.Info("Created test suite: " + testsuiteid);
                    return req.CreateResponse(HttpStatusCode.OK, "");
                }
                else
                {
                    log.Info("Test Suite Not Created");
                    return Utility.CreateErrorResponse(req, "Test Suite Not Created");
                }
            }
            else
            {
                var jsonPatch = new JsonPatch();
                bool hasTags = false;
                foreach (JProperty property in ((JObject)originalWI["resource"]["fields"]).Properties())
                {
                    log.Info(property.Name + " - " + Utility.JSONEncode(property.Value.ToString()));

                    if (property.Name == "System.Tags")
                    {
                        jsonPatch.Add("add", "/fields/System.Tags", null, Utility.JSONEncode(property.Value.ToString() + "; OriginalID=" + workItemID));
                        hasTags = true;
                    }
                    else if (property.Name == "Microsoft.VSTS.TCM.TestSuiteAudit")
                    {
                        string value = property.Value.ToString();
                        string[] numbers = Regex.Split(value, @"\D+");
                        log.Info(value + " - " + numbers.Length.ToString());
                        foreach (string origWI in numbers)
                        {
                            int prova;
                            if (Int32.TryParse(origWI, out prova))
                            {
                                value = value.Replace(origWI, await Utility.GetDestinationWorkItemId(destVSTS, destPAT, destPrj, origWI, log));
                            }
                        }
                        jsonPatch.Add("add", "/fields/" + property.Name, null, value);
                    }
                    else if (property.Name.Contains("Kanban.Column") || property.Name.Contains("System.BoardColum"))
                    {
                        // skip Kanban.Column(s)
                        // should be moved to jsonPatch add
                    }
                    else
                    {
                        jsonPatch.ParseAndAddProperty(property);
                    }
                }
                if (!hasTags)
                {
                    jsonPatch.Add("add", "/fields/System.Tags", null, "OriginalID=" + workItemID);
                }

                if (originalWI["resource"]["relations"] == null)
                {
                    log.Info("+++No Relations+++");
                }
                else
                {
                    log.Info(originalWI["resource"]["relations"].GetType().ToString());

                    JArray relations = (JArray)originalWI["resource"]["relations"];
                    foreach (JObject relation in relations.Children())
                    {
                        foreach (JProperty property in relation.Properties())
                        {
                            log.Info(property.Name + " - " + Utility.JSONEncode(property.Value.ToString()));
                        }

                        string rel = relation.Properties().FirstOrDefault(x => x.Name == "rel").Value.ToString();
                        string relUrl = "";

                        if (rel == "AttachedFile")
                        {
                            // attachments

                            relUrl = relation.Properties().FirstOrDefault(x => x.Name == "url").Value.ToString();
                            byte[] attachment = await Utility.GetSourceAttachment(relUrl + "?api-version=4.1", destPAT, log);
                            log.Info("Attachment length: " + attachment.Length.ToString());

                            string fileName = ((JObject)relation.Properties().FirstOrDefault(x => x.Name == "attributes").Value).Properties().FirstOrDefault(x => x.Name == "name").Value.ToString();
                            log.Info(fileName);
                            string jsonAttach = await Utility.CreateAttachment(destVSTS, destPAT, fileName, attachment, log);
                            JObject jsonObj = JObject.Parse(jsonAttach);

                            relUrl = Uri.UnescapeDataString((string)jsonObj["url"]);
                            log.Info(relUrl);

                        }
                        else
                        {
                            relUrl = await Utility.GetLinkedUrl(destVSTS, destPAT, destPrj, relation, log);
                        }

                        jsonPatch.AddCollection("add", "/relations/-", "",
                            "{" +
                            "  \"rel\": \"" + rel + "\"," +
                            "  \"url\": \"" + relUrl + "\"," +
                            "  \"attributes\": " + relation.Properties().FirstOrDefault(x => x.Name == "attributes").Value.ToString() +
                            "}");
                    }
                }
                log.Info(jsonPatch.ToString());

                try
                {
                    await CreateWorkItem(destVSTS, destPAT, destPrj, workItemType, jsonPatch, log);
                    return req.CreateResponse(HttpStatusCode.OK, "");
                }
                catch (Exception ex)
                {
                    log.Info(ex.Message);
                    return Utility.CreateErrorResponse(req, ex.Message);
                }
            }
        }

        private static string GetRootTestSuite(string destSuites)
        {
            string id = null;
            JObject destTestSuites = JObject.Parse(destSuites);
            JArray values = (JArray)destTestSuites["value"];
            foreach (JObject value in values.Children())
            {
                if (!value.Properties().Any(x => x.Name == "parent"))
                {
                    id = value.Properties().FirstOrDefault(x => x.Name == "id").Value.ToString();
                    break;
                }
            }
            return id;
        }

        private static async Task CreateWorkItem(string VSTS, string PAT, string TeamProject, string WorkItemType, JsonPatch jsonPatch, TraceWriter log)
        {
            // https://docs.microsoft.com/en-us/rest/api/vsts/wit/work%20items/create?view=vsts-rest-4.1


            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                var jsonContent = jsonPatch.StringContent();
                var postUri = VSTS + "/" + TeamProject + "/_apis/wit/workitems/$" + WorkItemType + "?api-version=4.1";
                log.Info(postUri);

                using (HttpResponseMessage response = await client.PostAsync(
                           postUri, jsonContent))
                {
                    response.EnsureSuccessStatusCode();
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                }
            }
        }

        private static void SetAuthentication(HttpClient client, string PAT)
        {
            client.DefaultRequestHeaders.Accept.Add(
                                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json-patch+json"));

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(
                    System.Text.ASCIIEncoding.ASCII.GetBytes(
                        string.Format("{0}:{1}", "", PAT))));
        }
    }
}