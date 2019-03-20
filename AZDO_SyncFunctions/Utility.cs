using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using System.Net;

namespace AZDO_SyncFunctions
{
    public class Utility
    {
        static string sourceTeamProject;
        static string destinationTeamProject;
        public static void SetVSTSAccountInfo(string sourceVSTS, string sourcePrj, out string destVSTS, out string destPrj, out string PAT)
        {
            try
            {
                destinationTeamProject = ConfigurationManager.AppSettings["destinationTeamProject"];
                sourceTeamProject = sourcePrj;

                destVSTS = ConfigurationManager.AppSettings["destinationVSTS"];
                destPrj = Uri.EscapeDataString(destinationTeamProject);
                PAT = ConfigurationManager.AppSettings["PersonalAccessToken"];
            }
            catch (Exception)
            {
                //initialize variables with default values
                PAT = "";
                destPrj = "";
                destVSTS = "";
            }
        }

        public static void SetVSTSAccountInfoForTestRun(out string sourceVSTS, out string sourcePrj, out string destVSTS, out string destPrj, out string PAT)
        {
            try
            {
                sourcePrj = Uri.EscapeDataString(ConfigurationManager.AppSettings["destinationTeamProject"]);
                destPrj = Uri.EscapeDataString(ConfigurationManager.AppSettings["destinationTeamProjectForTestRunMigrations"]);

                sourceVSTS = ConfigurationManager.AppSettings["destinationVSTS"];
                destVSTS = ConfigurationManager.AppSettings["destinationVSTSForTestRunMigrations"];

                PAT = ConfigurationManager.AppSettings["PersonalAccessToken"];
            }
            catch (Exception)
            {
                //initialize variables with default values
                sourceVSTS = "";   // Account name
                destVSTS = "";   // Account name
                sourcePrj = "";   // Project name
                destPrj = "";   // Project name

                PAT = "";
            }
        }

        public static string ProcessAreasAndIterations(string source)
        {
            string teamproject;
            string otherinfo;

            if (source.Contains('\\'))
            {
                teamproject = source.Substring(0, source.IndexOf('\\'));
                otherinfo = source.Substring(source.IndexOf('\\'));
            }
            else
            {
                teamproject = source;
                otherinfo = "";
            }

            if (teamproject == sourceTeamProject)
                return destinationTeamProject + otherinfo;
            else
                return sourceTeamProject + otherinfo;
        }

        public static string JSONEncode(string source)
        {
            source = source.Replace("\\", "\\\\");
            source = source.Replace("\"", "\\\"");
            return source;
        }
        public static async Task<string> GetDestinationWorkItemId(string VSTS, string PAT, string TeamProject, string originalWorkItemId, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/wit/wiql/query%20by%20wiql?view=vsts-rest-4.1

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                string jsonQuery =
                    "{\"query\": \"SELECT [System.Id] " +
                                  "FROM workitems " +
                                  "WHERE " +
                                  "[System.TeamProject] = @project " +
                                  "AND [System.WorkItemType] <> '' " +
                                  "AND [System.Tags] CONTAINS 'OriginalID=" + originalWorkItemId + "'\"}";

                log.Info(jsonQuery);
                var jsonContent = new StringContent(jsonQuery, Encoding.UTF8, "application/json");

                var postUri = VSTS + "/" + TeamProject + "/_apis/wit/wiql?api-version=4.1";
                log.Info(postUri);

                using (HttpResponseMessage response = await client.PostAsync(
                           postUri, jsonContent))
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                    response.EnsureSuccessStatusCode();

                    if (responseBody.IndexOf("workItems\":[{\"id\":") > 0)
                    {
                        string value = responseBody.Substring(responseBody.IndexOf("workItems\":[{\"id\":") + 18);
                        value = value.Substring(0, value.IndexOf(","));
                        log.Info(value);
                        return Uri.UnescapeDataString(value);
                    }
                    else
                    {
                        return "";
                    }
                }
            }
        }

        public static async Task<string> GetSourceWorkItemId(string VSTS, string PAT, string TeamProject, string WorkItemId, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/wit/work%20items/get%20work%20item?view=vsts-rest-5.0

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                var getUri = VSTS + "/" + TeamProject + "/_apis/wit/workitems/" + WorkItemId + "?fields=System.Tags&api-version=4.1";
                log.Info(getUri);

                using (HttpResponseMessage response = await client.GetAsync(
                           getUri))
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                    response.EnsureSuccessStatusCode();

                    if (responseBody.IndexOf("OriginalID=") > 0)
                    {
                        string returnValue = "";
                        string value = responseBody.Substring(responseBody.IndexOf("OriginalID=") + 11);

                        for (int i = 0; i < value.Length; i++)
                        {
                            if (value[i] < '0' || value[i] > '9')
                            {
                                returnValue = value.Substring(0, i);
                                break;
                            }
                        }
                        log.Info(returnValue);
                        return returnValue;
                    }
                    else
                    {
                        return "";
                    }
                }
            }
        }

        public static async Task<string> CreateTestPlan(string VSTS, string PAT, string TeamProject, string WorkItemId, string title, string areapath, string iteration, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/test/test%20%20plans/create?view=vsts-rest-5.0

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                var postUri = VSTS + "/" + TeamProject + "/_apis/test/plans?api-version=5.0-preview.2";
                log.Info(postUri);

                string postContent = "{ " +
                    "\"name\": \"" + title + "\", " +
                    "\"area\": { " +
                       "\"name\": \"" + Utility.JSONEncode(areapath) + "\"}, " +
                    "\"iteration\": \"" + Utility.JSONEncode(iteration) + "\" " +
                    "}";

                log.Info(postContent);

                var jsonContent = new StringContent(postContent, Encoding.UTF8, "application/json");


                using (HttpResponseMessage response = await client.PostAsync(
                           postUri, jsonContent))
                {
                    log.Info(response.StatusCode.ToString());
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                    response.EnsureSuccessStatusCode();
                    JObject newPlan = JObject.Parse(responseBody);
                    string id = Uri.UnescapeDataString((string)newPlan["id"]);

                    await Utility.UpdateWorkItemOriginalIDTag(VSTS, PAT, TeamProject, id, WorkItemId, log);
                    return id;
                }
            }
        }

        public static async Task<string> UpdateTestCase(string VSTS, string PAT, string TeamProject, string originalTestCase, TraceWriter log)
        {
            JObject testcase = JObject.Parse(originalTestCase);
            string testCaseId = await Utility.GetDestinationWorkItemId(VSTS, PAT, TeamProject, testcase["testCase"]["id"].ToString(), log);

            JObject destTestCase = JObject.Parse(await Utility.GetDestinationWorkItem(VSTS, PAT, TeamProject, testCaseId, log));

            try
            {
                testcase["testCase"]["id"] = destTestCase["id"].ToString();
                testcase["testCase"]["url"] = destTestCase["_links"]["self"]["href"].ToString();
                testcase["testCase"]["weburl"] = destTestCase["_links"]["html"]["href"].ToString();
            }
            catch (Exception ex)
            {
                log.Info(ex.Message);
                throw;
            }
            return testcase.ToString();

        }

        public static async Task<string> GetTestCaseById(string VSTS, string PAT, string TeamProject, string TestSuiteId, string TestCaseId, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/test/test%20%20suites/get%20test%20case%20by%20id?view=vsts-rest-5.0

            string testPlanId = await Utility.GetTestPlanFromGenericTestSuite(VSTS, TeamProject, PAT, TestSuiteId, log);
            log.Info("Test Plan Id: " + testPlanId);

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                var getUri = VSTS + "/" + TeamProject + "/_apis/test/Plans/" + testPlanId + "/suites/" + TestSuiteId + "/testcases/" + TestCaseId + "?&api-version=5.0-preview.3";
                log.Info(getUri);

                using (HttpResponseMessage response = await client.GetAsync(
                           getUri))
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                    response.EnsureSuccessStatusCode();
                    return responseBody;
                }
            }
        }

        public static async Task<bool> AddTestCaseToTestSuite(string VSTS, string PAT, string TeamProject, string TestSuiteId, string TestCaseId, string TestCase, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/test/test%20%20suites/add?view=vsts-rest-5.0

            string json = "{ \"value\": [ " + TestCase + " ], \"count\" : 1 }";
            string TestPlan = await Utility.GetTestPlanFromGenericTestSuite(VSTS, TeamProject, PAT, TestSuiteId, log);

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                var postUri = VSTS + "/" + TeamProject + "/_apis/test/plans/" + TestPlan + "/suites/" + TestSuiteId + "/testcases/" + TestCaseId + "/?api-version=5.0-preview.3";
                log.Info(postUri);
                log.Info(json);

                var jsonContent = new StringContent(json, Encoding.UTF8, "application/json");


                using (HttpResponseMessage response = await client.PostAsync(
                           postUri, jsonContent))
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                    response.EnsureSuccessStatusCode();
                }
            }
            return true;
        }

        public static async Task<bool> DeleteTestSuite(string VSTS, string PAT, string TeamProject, string TestSuiteId, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/test/test%20%20suites/delete?view=vsts-rest-5.0

            string testPlanId = await Utility.GetTestPlanFromGenericTestSuite(VSTS, TeamProject, PAT, TestSuiteId, log);

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                var deleteUri = VSTS + "/" + TeamProject + "/_apis/test/plans/" + testPlanId + "/suites/" + TestSuiteId + "/?api-version=5.0-preview.3";
                log.Info(deleteUri);

                using (HttpResponseMessage response = await client.DeleteAsync(deleteUri))
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                    response.EnsureSuccessStatusCode();
                }
            }
            return true;
        }

        public static async Task<bool> RemoveTestCaseFromTestSuite(string VSTS, string PAT, string TeamProject, string TestSuiteId, string TestCaseId, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/test/test%20%20suites/remove%20test%20cases%20from%20suite%20url?view=vsts-rest-5.0

            string testPlanId = await Utility.GetTestPlanFromGenericTestSuite(VSTS, TeamProject, PAT, TestSuiteId, log);

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                var deleteUri = VSTS + "/" + TeamProject + "/_apis/test/plans/" + testPlanId + "/suites/" + TestSuiteId + "/testcases/" + TestCaseId + "/?api-version=5.0-preview.3";
                log.Info(deleteUri);

                using (HttpResponseMessage response = await client.DeleteAsync(deleteUri))
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                    response.EnsureSuccessStatusCode();
                }
            }
            return true;
        }

        public static async Task<string> CreateTestSuite(string VSTS, string PAT, string TeamProject, string TestPlan, string ParentTestSuite, string OriginalID, string json, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/test/test%20%20suites/create?view=vsts-rest-5.0

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                var postUri = VSTS + "/" + TeamProject + "/_apis/test/plans/" + TestPlan + "/suites/" + ParentTestSuite + "/?api-version=5.0-preview.3";
                log.Info(postUri);

                log.Info(json);

                var jsonContent = new StringContent(json, Encoding.UTF8, "application/json");


                using (HttpResponseMessage response = await client.PostAsync(
                           postUri, jsonContent))
                {
                    log.Info(response.StatusCode.ToString());
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                    response.EnsureSuccessStatusCode();
                    string id = GetFirstTestSuite(responseBody);
                    await Utility.UpdateWorkItemOriginalIDTag(VSTS, PAT, TeamProject, id, OriginalID, log);
                    return id;
                }
            }
        }

        public static HttpResponseMessage CreateErrorResponse(HttpRequestMessage req, string message)
        {
            var resp = req.CreateResponse(HttpStatusCode.BadRequest);
            resp.Headers.Add("X-Exception", message);
            return resp;
        }

        private static string GetFirstTestSuite(string destSuites)
        {
            string id = null;
            JObject destTestSuites = JObject.Parse(destSuites);
            JArray values = (JArray)destTestSuites["value"];
            foreach (JObject value in values.Children())
            {
                id = value.Properties().FirstOrDefault(x => x.Name == "id").Value.ToString();
                break;
            }
            return id;
        }


        public static async Task<bool> UpdateWorkItemOriginalIDTag(string VSTS, string PAT, string TeamProject, string WorkItemId, string OriginalId, TraceWriter log)
        {
            string workitem = await GetDestinationWorkItem(VSTS, PAT, TeamProject, WorkItemId, log);
            JObject wi = JObject.Parse(workitem);
            string rev = Uri.UnescapeDataString((string)wi["rev"]);

            string tags;
            if (workitem.Contains("System.Tags"))
            {
                tags = Uri.UnescapeDataString((string)wi["fields"]["System.Tags"]);
                if (tags.Contains("OriginalID="))
                {
                    log.Info("Tags already contains OriginalID");
                }
                else
                {
                    tags = "OriginalID=" + OriginalId + "; " + tags;
                }
            }
            else
            {
                tags = "OriginalID=" + OriginalId;
            }

            var jsonPatch = new JsonPatch();
            jsonPatch.Add("test", "/rev", null, rev);
            jsonPatch.Add("add", "/fields/System.Tags", null, tags);

            if (jsonPatch.ToString() != "[]")
            {
                log.Info(jsonPatch.ToString());
                try
                {
                    await UpdateWorkItem(VSTS, PAT, TeamProject, WorkItemId, jsonPatch, log);
                    return true;
                }
                catch (Exception ex)
                {
                    log.Info(ex.Message);
                    return false;
                }
            }
            else
            {
                log.Info("+++ Empty JSON Patch +++");
                return true;
            }
        }

        public static async Task UpdateWorkItem(string VSTS, string PAT, string TeamProject, string WorkItemId, JsonPatch jsonPatch, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/wit/work%20items/update?view=vsts-rest-4.1

            using (HttpClient client = new HttpClient())
            {
                Utility.SetAuthenticationPatch(client, PAT);

                var jsonContent = jsonPatch.StringContent();
                var postUri = VSTS + "/" + TeamProject + "/_apis/wit/workitems/" + WorkItemId + "?api-version=4.1";
                log.Info(postUri);

                using (HttpResponseMessage response = await client.PatchAsync(
                           postUri, jsonContent))
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                    response.EnsureSuccessStatusCode();
                }
            }
        }


        public static async Task<string> GetDestinationWorkItem(string VSTS, string PAT, string TeamProject, string WorkItemId, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/wit/work%20items/get%20work%20item?view=vsts-rest-4.1

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                var getUri = VSTS + "/" + TeamProject + "/_apis/wit/workitems/" + WorkItemId + "?$expand=all&api-version=4.1";
                log.Info(getUri);

                using (HttpResponseMessage response = await client.GetAsync(
                           getUri))
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                    response.EnsureSuccessStatusCode();
                    return responseBody;
                }
            }
        }

        public static async Task<byte[]> GetSourceAttachment(string Url, string PAT, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/wit/attachments/get?view=vsts-rest-4.1

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);
                client.DefaultRequestHeaders.Accept.Add(
                                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/octet-stream"));

                log.Info(Url);

                using (HttpResponseMessage response = await client.GetAsync(
                           Url))
                {
                    response.EnsureSuccessStatusCode();
                    byte[] responseBody = await response.Content.ReadAsByteArrayAsync();
                    //log.Info(responseBody);
                    return responseBody;
                }
            }
        }

        public static async Task<string> CreateAttachment(string VSTS, string PAT, string FileName, byte[] FileContent, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/wit/attachments/create?view=vsts-rest-4.1

            using (HttpClient client = new HttpClient())
            {
                Utility.SetAuthentication(client, PAT);

                var content = new ByteArrayContent(FileContent);
                var postUri = VSTS + "/_apis/wit/attachments/filename" + FileName + "?api-version=4.1";
                log.Info(postUri);

                using (HttpResponseMessage response = await client.PostAsync(
                           postUri, content))
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                    response.EnsureSuccessStatusCode();
                    return responseBody;
                }
            }
        }


        public static void SetAuthenticationPatch(HttpClient client, string PAT)
        {
            client.DefaultRequestHeaders.Accept.Add(
                                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json-patch+json"));

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(
                    System.Text.ASCIIEncoding.ASCII.GetBytes(
                        string.Format("{0}:{1}", "", PAT))));
        }

        public static void SetAuthentication(HttpClient client, string PAT)
        {
            //client.DefaultRequestHeaders.Accept.Add(
            //                        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(
                    System.Text.ASCIIEncoding.ASCII.GetBytes(
                        string.Format("{0}:{1}", "", PAT))));
        }

        public static async Task<string> GetLinkedUrl(string VSTS, string PAT, string TeamProject, JObject relation, TraceWriter log)
        {
            string url = relation.Properties().FirstOrDefault(x => x.Name == "url").Value.ToString();

            if (url.Contains("workItems"))
            {
                url = url.Substring(0, url.LastIndexOf('/') + 1) + await Utility.GetDestinationWorkItemId(VSTS, PAT, TeamProject, GetWorkItemIdFromLinkUrl(url), log);
            }

            return url;
        }

        public static string GetWorkItemIdFromLinkUrl(string url)
        {
            return url.Substring(url.LastIndexOf('/') + 1);
        }

        public static async Task<string> GetRelationNumber(string VSTS, string PAT, string TeamProject, JObject relation, TraceWriter log)
        {
            string url = relation.Properties().FirstOrDefault(x => x.Name == "url").Value.ToString();

            if (url.Contains("workItems"))
            {
                url = url.Substring(0, url.LastIndexOf('/') + 1) + await Utility.GetDestinationWorkItemId(VSTS, PAT, TeamProject, url.Substring(url.LastIndexOf('/') + 1), log);
            }

            return url;
        }

        public static async Task<string> GetTestSuitesFromTestPlan(string VSTS, string TeamProject, string PAT, string TestPlanId, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/test/test%20%20suites/get%20test%20suites%20for%20plan?view=vsts-rest-5.0

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                var getUri = VSTS + "/" + TeamProject + "/_apis/test/Plans/" + TestPlanId + "/suites?api-version=5.0-preview.3";
                log.Info(getUri);

                using (HttpResponseMessage response = await client.GetAsync(
                           getUri))
                {
                    try
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        log.Info(responseBody);
                        response.EnsureSuccessStatusCode();
                        return responseBody;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }

            }
        }

        public static async Task<string> GetTestPlanFromRootTestSuite(string VSTS, string TeamProject, string PAT, string TestSuiteId, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/test/test%20%20plans/list?view=vsts-rest-5.0

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                var getUri = VSTS + "/" + TeamProject + "/_apis/test/plans?filterActivePlans=true&api-version=5.0-preview.2";
                log.Info(getUri);

                using (HttpResponseMessage response = await client.GetAsync(
                           getUri))
                {
                    try
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        log.Info(responseBody);
                        response.EnsureSuccessStatusCode();

                        JObject testPlans = JObject.Parse(responseBody);

                        JArray values = (JArray)testPlans["value"];
                        foreach (JObject value in values.Children())
                        {
                            JObject rootSuite = (JObject)value.Properties().FirstOrDefault(x => x.Name == "rootSuite").Value;
                            string rootSuiteId = rootSuite.Properties().FirstOrDefault(x => x.Name == "id").Value.ToString();

                            if (rootSuiteId == TestSuiteId)
                            {
                                string planId = value.Properties().FirstOrDefault(x => x.Name == "id").Value.ToString();
                                log.Info("Found Plan: " + planId);
                                return planId;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Info(ex.Message);
                        throw;
                    }
                }
                return null;
            }
        }

        public static async Task<string> GetTestPlanFromGenericTestSuite(string VSTS, string TeamProject, string PAT, string TestSuiteId, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/test/test%20%20plans/list?view=vsts-rest-5.0

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                var getUri = VSTS + "/" + TeamProject + "/_apis/test/plans?filterActivePlans=true&api-version=5.0-preview.2";
                log.Info(getUri);

                using (HttpResponseMessage response = await client.GetAsync(
                           getUri))
                {
                    try
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        log.Info(responseBody);
                        response.EnsureSuccessStatusCode();

                        JObject testPlans = JObject.Parse(responseBody);

                        JArray values = (JArray)testPlans["value"];
                        foreach (JObject value in values.Children())
                        {
                            string planId = value.Properties().FirstOrDefault(x => x.Name == "id").Value.ToString();
                            string destSuites = await Utility.GetTestSuitesFromTestPlan(VSTS, TeamProject, PAT, planId, log);
                            if (IsTestSuitePresent(destSuites, TestSuiteId))
                            {
                                return planId;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Info(ex.Message);
                        throw;
                    }
                }
                return null;
            }
        }

        public static async Task<string> GetRequirementId(string VSTS, string TeamProject, string PAT, string TestPlanId, string TestSuiteId, TraceWriter log)
        {
            //https://docs.microsoft.com/en-us/rest/api/vsts/test/test%20%20suites/get%20test%20suite%20by%20id?view=vsts-rest-5.0

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                var getUri = VSTS + "/" + TeamProject + "/_apis/test/plans/" + TestPlanId + "/suites/" + TestSuiteId + "?api-version=5.0-preview.3";
                log.Info(getUri);

                using (HttpResponseMessage response = await client.GetAsync(
                           getUri))
                {
                    try
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        log.Info(responseBody);
                        response.EnsureSuccessStatusCode();
                        JObject testsuite = JObject.Parse(responseBody);

                        return testsuite["requirementId"].ToString();
                    }
                    catch (Exception ex)
                    {
                        log.Info(ex.Message);
                        throw;
                    }
                }
            }
        }
        public static bool IsTestSuitePresent(string destSuites, string testSuiteId)
        {
            string id = null;
            JObject destTestSuites = JObject.Parse(destSuites);
            JArray values = (JArray)destTestSuites["value"];
            foreach (JObject value in values.Children())
            {
                id = value.Properties().FirstOrDefault(x => x.Name == "id").Value.ToString();
                if (id == testSuiteId)
                {
                    return true;
                }
            }
            return false;
        }

        public static async Task<string> PostJsonString(string PAT, string url, string json, TraceWriter log)
        {
            using (HttpClient client = new HttpClient())
            {
                Utility.SetAuthentication(client, PAT);

                log.Info("POST " + url);

                var jsonContent = new StringContent(json, Encoding.UTF8, "application/json");

                using (HttpResponseMessage response = await client.PostAsync(url, jsonContent))
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                    response.EnsureSuccessStatusCode();
                    return responseBody;
                }
            }
        }

        public static async Task<string> PatchJsonString(string PAT, string url, string json, TraceWriter log)
        {
            using (HttpClient client = new HttpClient())
            {
                Utility.SetAuthentication(client, PAT);

                log.Info("POST " + url);

                var jsonContent = new StringContent(json, Encoding.UTF8, "application/json");

                using (HttpResponseMessage response = await client.PatchAsync(url, jsonContent))
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                    response.EnsureSuccessStatusCode();
                    return responseBody;
                }
            }
        }

        public static async Task<string> GetJsonFromUrl(string PAT, string url, TraceWriter log)
        {
            using (HttpClient client = new HttpClient())
            {
                Utility.SetAuthentication(client, PAT);

                log.Info("GET " + url);

                using (HttpResponseMessage response = await client.GetAsync(url))
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.Info(responseBody);
                    response.EnsureSuccessStatusCode();
                    return responseBody;
                }
            }
        }

    }
}
