
﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.Runtime.Internal;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SageMaker;
using Amazon.SageMaker.Model;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using DotStep.Core;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Primitives;
using Tag = Amazon.SageMaker.Model.Tag;

namespace ModelBuilder.StateMachines
{
    public static class MethodExtensions
    {
        public static string S3Bucket(this string s3Location) => s3Location.Split('/')[2];

        public static string S3Key(this string s3Location) =>
            s3Location.Replace($"s3://{s3Location.S3Bucket()}/", string.Empty);
    }


    public sealed class ProvisionScene : StateMachine<ProvisionScene.Initialize>
    {
        public class Context : IContext
        {
            [DotStep.Core.Required]
            public List<string> ClassNames { get; set; }
            [DotStep.Core.Required]
            public string SceneImageLocation { get; set; }
            [DotStep.Core.Required]
            public string Region { get; set; }
            [DotStep.Core.Required]
            public string CameraKey { get; set; }
            public string CameraBucket { get; set; }

            public string SceneBackgroundLocation { get; set; }
            public bool GenerateSceneBackground { get; set; }
           
            public string WorkteamArn { get; set; }
            public string LabelingRoleArn { get; set; }

            public string SceneProvisioningJobId { get; set; }
            public string SceneProvisioningJobWorkspace { get; set; }
            public string InputManifestLocation { get; set; }
            public string UiTemplateLocation { get; set; }

            public int LabelingJobPercentComplete { get; set; }


            public string SceneBackgroundGenerationQueueUrl { get; set; }
            public int BackgroundImagePercentComplete { get; set; }

            public int ImagesGeneratedPerClass { get; set; }

            public int NumberOfTrainingSamples { get; set; }

            public string SceneCode { get; set; }
            public int TrainingJobPercentComplete { get; set; }
            public int EndpointPercentComplete { get; set; }

            public int MotionThreshold { get; set; }
        }



        [DotStep.Core.Action(ActionName = "*")]
        public sealed class Initialize : TaskState<Context, CreateSegmentationLabelingJob>
        {
            IAmazonSimpleSystemsManagement ssm = new AmazonSimpleSystemsManagementClient();
            IAmazonS3 s3 = new AmazonS3Client();

            public override async Task<Context> Execute(Context context)
            {
                context.SceneProvisioningJobId = $"spj-{Guid.NewGuid().ToString().Substring("e9857953-2fc2-477e-b347-".Length, "6053c7f7b710".Length).ToLower()}";
                
                if (context.ClassNames.Count < 1)
                    throw new Exception("ClassNames are required.");

                var parameters = await ssm.GetParametersAsync(new GetParametersRequest
                {
                    Names = new List<string>
                    {
                        "/scene-provision/WorkteamArn",
                        "/scene-provision/LabelingRoleArn",
                        "/scene-provision/JobWorkspace",
                        "/scene-provision/SceneBackgroundGenerationQueueUrl"
                    }
                });

               
                context.WorkteamArn = parameters.Parameters.Single(p => p.Name == "/scene-provision/WorkteamArn").Value;
                context.LabelingRoleArn = parameters.Parameters.Single(p => p.Name == "/scene-provision/LabelingRoleArn").Value;
                context.SceneProvisioningJobWorkspace =
                    $"{parameters.Parameters.Single(p => p.Name == "/scene-provision/JobWorkspace").Value}{context.SceneProvisioningJobId}/";
                
                context.GenerateSceneBackground = string.IsNullOrEmpty(context.SceneBackgroundLocation);

                if (context.GenerateSceneBackground)
                    context.SceneBackgroundGenerationQueueUrl = parameters.Parameters
                        .Single(p => p.Name == "/scene-provision/SceneBackgroundGenerationQueueUrl").Value;
                else
                {
                    await s3.CopyObjectAsync(context.SceneBackgroundLocation.S3Bucket(),