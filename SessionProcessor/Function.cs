using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.Json;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(JsonSerializer))]

namespace SessionProcessor
{
    public class Function
    {
        private readonly IAmazonSimpleSystemsManagement ssm = new AmazonSimpleSystemsManagementClient();

        public async Task FunctionHandler(dynamic @event, ILambdaContext context)
        {
            var parameters = new List<Parameter>();
            string nextToken = null;

            Query:
            var parametersResult = await ssm.GetParametersByPathAsync(new GetParametersByPathRequest
            {
                Path = "/Cameras/",
                Recursive = true,
                NextToken = nextToken
            });
            parameters.AddRange(parametersResult.Parameters);
            nextToken = parametersResult.NextToken;
            if (!string.IsNullOrEmpty(nextToken))
                goto Query;

            var cameraKeys = parameters.Select(p => p.Name.Split('/')[2]).Distinct();

            foreach (var cameraKey in cameraKeys)
