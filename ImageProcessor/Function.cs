
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.S3;
using Amazon.S3.Util;
using Amazon.SageMakerRuntime;
using Amazon.SageMakerRuntime.Model;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Newtonsoft.Json;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using SixLabors.Primitives;
using Image = SixLabors.ImageSharp.Image;
using JsonSerializer = Amazon.Lambda.Serialization.Json.JsonSerializer;

[assembly: LambdaSerializer(typeof(JsonSerializer))]

namespace ImageProcessor
{
    public class Function
    {
        private readonly Dictionary<string, string> cameraParameters = new Dictionary<string, string>();
        private readonly IAmazonCloudWatch cloudWatch = new AmazonCloudWatchClient();
        private readonly IAmazonRekognition rekognition = new AmazonRekognitionClient();
        private readonly IAmazonS3 s3 = new AmazonS3Client();
        private readonly IAmazonSageMakerRuntime sageMakerRuntime = new AmazonSageMakerRuntimeClient();
        private readonly IAmazonSimpleSystemsManagement ssm = new AmazonSimpleSystemsManagementClient();

        public async Task FunctionHandler(S3Event s3Event, ILambdaContext context)
        {
            var tasks = new List<Task>();
            foreach (var record in s3Event.Records)
                tasks.Add(ProcessRecord(record, context));
            await Task.WhenAll(tasks);
        }

        public async Task ProcessRecord(S3EventNotification.S3EventNotificationRecord record, ILambdaContext context)
        {
            var cameraKey = record.S3.Object.Key.Split('/')[1];

            var s3GetResult = await s3.GetObjectAsync(record.S3.Bucket.Name, record.S3.Object.Key);

            var classNamesParameterName = $"/Cameras/{cameraKey}/ClassNames";
            var sceneCodeParameterName = $"/Cameras/{cameraKey}/SceneCode";
            var observationBoundingBoxParameterName = $"/Cameras/{cameraKey}/ObservationBoundingBox";

            if (!cameraParameters.ContainsKey(observationBoundingBoxParameterName))
                try
                {
                    var getResult = await ssm.GetParameterAsync(new GetParameterRequest
                    {
                        Name = observationBoundingBoxParameterName
                    });