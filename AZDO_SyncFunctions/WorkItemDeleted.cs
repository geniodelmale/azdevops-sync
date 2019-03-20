using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

namespace AZDO_SyncFunctions
{
    public static class WorkItemDeleted
    {
        [FunctionName("WorkItemDeleted")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            log.Info($"++++++++++WorkItemDelete was triggered!");

            HttpContent requestContent = req.Content;
            string jsonContent = requestContent.ReadAsStringAsync().Result;
            log.Info(jsonContent);

            JObject originalWI = JObject.Parse(jsonContent);
            string workItemID = (string)originalWI["resource"]["id"];

            log.Info("Original ID=" + workItemID);

            string destVSTS;
            string destPrj;
            string destPAT;

            Utility.SetVSTSAccountInfo("", "", out destVSTS, out destPrj, out destPAT);

            //log.Info(destVSTS);
            //log.Info(destPrj);
            //log.Info(destPAT);

            string destWorkItemID = await Utility.GetDestinationWorkItemId(destVSTS, destPAT, destPrj, workItemID, log);

            if (destWorkItemID == "")
            {
                log.Info("WorkItem to be deleted not found...");
                return req.CreateResponse(HttpStatusCode.OK, "");
            }
            else
            {
                try
                {
                    await DeleteWorkItem(destVSTS, destPAT, destPrj, destWorkItemID, log);
                    return req.CreateResponse(HttpStatusCode.OK, "");
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("404 (Not Found)"))
                    {
                        log.Info("WorkItem to be deleted not found...");
                        return req.CreateResponse(HttpStatusCode.OK, "");
                    }
                    else
                    {
                        log.Info(ex.Message);
                        return Utility.CreateErrorResponse(req, ex.Message);
                    }
                }
            }
        }

        private static async Task DeleteWorkItem(string VSTS, string PAT, string TeamProject, string WorkItemID, TraceWriter log)
        {
            // https://docs.microsoft.com/en-us/rest/api/vsts/wit/work%20items/delete?view=vsts-rest-4.1

            using (HttpClient client = new HttpClient())
            {
                SetAuthentication(client, PAT);

                var deleteUri = VSTS + "/" + TeamProject + "/_apis/wit/workitems/" + WorkItemID + "?api-version=4.1";
                log.Info(deleteUri);

                using (HttpResponseMessage response = await client.DeleteAsync(deleteUri))
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
