using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace ContractChatAgent.Functions;

public class ContractChat
{
    [Function("ContractChat")]
    public HttpResponseData Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "contract-chat")] HttpRequestData req)
    {
        return req.CreateResponse(HttpStatusCode.NotImplemented);
    }
}
