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
            var p