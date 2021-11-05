
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
                        context.SceneBackgroundLocation.S3Key(),
                        context.SceneProvisioningJobWorkspace.S3Bucket(),
                        context.SceneProvisioningJobWorkspace.S3Key() + "scene-background.jpg");
                    context.BackgroundImagePercentComplete = 100;
                    context.SceneBackgroundLocation = context.SceneProvisioningJobWorkspace + "scene-background.jpg";
                }

                if (context.MotionThreshold <= 0)
                    context.MotionThreshold = 200;

                var inputManifestBody = $"{{\"source-ref\": \"{context.SceneProvisioningJobWorkspace}input-scene.jpg\"}}";
                context.InputManifestLocation = $"{context.SceneProvisioningJobWorkspace}input-manifest.json";

                context.UiTemplateLocation = $"{context.SceneProvisioningJobWorkspace}Segmentation.xhtml";
                var uiTemplateBody = File.OpenText("Segmentation.xhtml").ReadToEnd();

                if (string.IsNullOrEmpty(context.SceneCode))
                {
                    var locationParts = context.SceneImageLocation.Split('/');
                    context.SceneCode = locationParts[locationParts.Length - 1];
                    context.SceneCode = context.SceneCode.Split('.')[0];
                    context.SceneCode = context.SceneCode.Replace("-", string.Empty).Replace("_", string.Empty).ToLower();
                }

                if (string.IsNullOrEmpty(context.CameraBucket))
                    context.CameraBucket = context.SceneProvisioningJobWorkspace.S3Bucket();

                await Task.WhenAll(new List<Task>
                {
                    ssm.PutParameterAsync(new PutParameterRequest
                    {
                        Name = $"/Cameras/{context.CameraKey}/ClassNames",
                        Value = string.Join(',', context.ClassNames),
                        Type = Amazon.SimpleSystemsManagement.ParameterType.String,
                        Overwrite = true
                    }),
                    ssm.PutParameterAsync(new PutParameterRequest
                    {
                        Name = $"/Cameras/{context.CameraKey}/SceneCode",
                        Value = context.SceneCode,
                        Type = Amazon.SimpleSystemsManagement.ParameterType.String,
                        Overwrite = true
                    }),
                    ssm.PutParameterAsync(new PutParameterRequest
                    {
                        Name = $"/Cameras/{context.CameraKey}/CameraBucket",
                        Value = context.CameraBucket,
                        Type = Amazon.SimpleSystemsManagement.ParameterType.String,
                        Overwrite = true
                    }),
                    ssm.PutParameterAsync(new PutParameterRequest
                    {
                        Name = $"/Cameras/{context.CameraKey}/Enabled",
                        Value = "False",
                        Type = Amazon.SimpleSystemsManagement.ParameterType.String,
                        Overwrite = true
                    }),
                    ssm.PutParameterAsync(new PutParameterRequest
                    {
                        Name = $"/Cameras/{context.CameraKey}/MotionThreshold",
                        Value = Convert.ToString(context.MotionThreshold),
                        Type = Amazon.SimpleSystemsManagement.ParameterType.String,
                        Overwrite = true
                    }),
                    s3.PutObjectAsync(new PutObjectRequest
                    {
                        BucketName = context.UiTemplateLocation.S3Bucket(),
                        Key = context.UiTemplateLocation.S3Key(),
                        ContentType = "application/xhtml+xml",
                        ContentBody = uiTemplateBody
                    }),
                    s3.PutObjectAsync(new PutObjectRequest
                    {
                        BucketName = context.InputManifestLocation.S3Bucket(),
                        Key = context.InputManifestLocation.S3Key(),
                        ContentType = "application/json",
                        ContentBody = inputManifestBody
                    }),
                    s3.PutObjectAsync(new PutObjectRequest
                    {
                        BucketName = context.SceneProvisioningJobWorkspace.S3Bucket(),
                        Key = context.SceneProvisioningJobWorkspace.S3Key() + "classes.csv",
                        ContentType = "text/csv",
                        ContentBody = string.Join(',', context.ClassNames)
                    }),
                    s3.CopyObjectAsync(new CopyObjectRequest
                    {
                        SourceBucket = context.SceneImageLocation.S3Bucket(),
                        SourceKey = context.SceneImageLocation.S3Key(),
                        DestinationBucket = context.SceneProvisioningJobWorkspace.S3Bucket(),
                        DestinationKey = $"{context.SceneProvisioningJobWorkspace.S3Key()}input-scene.jpg",
                        ContentType = "image/jpg"
                    })
                });

                if (context.ImagesGeneratedPerClass < 1)
                    context.ImagesGeneratedPerClass = 10;


                    
                return context;
            }
        }


        public sealed class CheckIfJobComplete : ChoiceState<ExtractPolygons>
        {
            public override List<Choice> Choices => new List<Choice>
            {
                new Choice<WaitBeforeCheckingLabelingJobStatus, Context>(c => c.LabelingJobPercentComplete < 100)
            };
        }

        public sealed class WaitBeforeCheckingLabelingJobStatus : WaitState<GetLabelingJobStatus>
        {
            public override int Seconds => 60;
        }

        [DotStep.Core.Action(ActionName = "*")]
        public sealed class GetLabelingJobStatus : TaskState<Context, CheckIfJobComplete>
        {
            readonly IAmazonSageMaker sageMaker = new AmazonSageMakerClient();
            public override async Task<Context> Execute(Context context)
            {

                var result = await sageMaker.DescribeLabelingJobAsync(
                    new DescribeLabelingJobRequest
                    {
                        LabelingJobName = context.SceneProvisioningJobId
                    });

                switch (result.LabelingJobStatus)
                {
                    case "InProgress":
                        context.LabelingJobPercentComplete = 50;
                        break;
                    case "Completed":
                        context.LabelingJobPercentComplete = 100;
                        break;
                    default: throw new Exception($"Labeling job status = {result.LabelingJobStatus}.");
                }
                Console.WriteLine($"Percent complete: {context.LabelingJobPercentComplete}");

                return context;
            }
        }

        [DotStep.Core.Action(ActionName = "*")]
        public sealed class CreateSegmentationLabelingJob : TaskState<Context, CheckIfJobComplete>
        {
            readonly IAmazonSageMaker sageMaker = new AmazonSageMakerClient();
            readonly IAmazonS3 s3 = new AmazonS3Client();

            public override async Task<Context> Execute(Context context)
            {

                Console.Write(JsonConvert.SerializeObject(context));

                //var resp = await sageMaker.DescribeLabelingJobAsync(new DescribeLabelingJobRequest
               // {
                //    LabelingJobName = "TestSegment"
                //});

                var labels = context.ClassNames.Select(className => new {label = className});
                

                var labelBody = "{\"document-version\":\"2018-11-28\",\"labels\": " + JsonConvert.SerializeObject(labels) + "}";
                var labelLocation = $"{context.SceneProvisioningJobWorkspace}SegmentationLabels.json";

                await s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = labelLocation.S3Bucket(),
                    Key = labelLocation.S3Key(),
                    ContentType = "application/json",
                    ContentBody = labelBody
                });

                //try
                //{
                    await sageMaker.CreateLabelingJobAsync(new CreateLabelingJobRequest
                    {
                        Tags = new AutoConstructedList<Tag>
                        {
                            new Tag
                            {
                                Key = "SceneProvisioningJobId",
                                Value = context.SceneProvisioningJobId
                            }
                        },
                        LabelingJobName = $"{context.SceneProvisioningJobId}",
                        LabelAttributeName = "Polygon-ref",
                        LabelCategoryConfigS3Uri = labelLocation,
                        StoppingConditions = new LabelingJobStoppingConditions
                        {
                            MaxPercentageOfInputDatasetLabeled = 100
                        },
                        RoleArn = context.LabelingRoleArn,
                        
                        HumanTaskConfig = new HumanTaskConfig
                        {
                            TaskKeywords = new List<string>
                            {
                                "Images",
                                "image segmentation"
                            },
                            PreHumanTaskLambdaArn = $"arn:aws:lambda:{context.Region}:432418664414:function:PRE-SemanticSegmentation",
                            TaskAvailabilityLifetimeInSeconds = 345600,
                            TaskTimeLimitInSeconds = 300,
                            //MaxConcurrentTaskCount = 1000,
                            AnnotationConsolidationConfig = new AnnotationConsolidationConfig
                            {
                                AnnotationConsolidationLambdaArn =
                                    $"arn:aws:lambda:{context.Region}:432418664414:function:ACS-SemanticSegmentation"
                            },
                            NumberOfHumanWorkersPerDataObject = 1,
                            TaskTitle = "Semantic segmentation",
                            TaskDescription = $"Draw a polygon on objects.",
                            WorkteamArn = context.WorkteamArn,
                            UiConfig = new UiConfig
                            {
                                UiTemplateS3Uri = context.UiTemplateLocation
                            }

                        },
                        InputConfig = new LabelingJobInputConfig
                        {
                            DataSource = new LabelingJobDataSource
                            {
                                S3DataSource = new LabelingJobS3DataSource
                                {
                                    ManifestS3Uri = context.InputManifestLocation
                                }
                            }
                        },
                        OutputConfig = new LabelingJobOutputConfig
                        {
                            S3OutputPath = $"{context.SceneProvisioningJobWorkspace}output/"
                        }
                    });
                    /*
                }
                catch (Exception e)
                {
                    Console.Write(e);
                    if (e.InnerException is HttpErrorResponseException sme)
                    {
                        var stream = sme.Response.ResponseBody.OpenResponse();
                        var sr = new StreamReader(stream);
                        var text = sr.ReadToEnd();
                        Console.WriteLine(text);
                    }
                }
                */
                return context;
            }
        }

        [DotStep.Core.Action(ActionName = "*")]
        [FunctionMemory(Memory = 3008)]
        [FunctionTimeout(Timeout = 900)]
        public sealed class ExtractPolygons : TaskState<Context, CheckIfBackgroundSceneExists>
        {
            readonly IAmazonS3 s3 = new AmazonS3Client();
            readonly IAmazonSQS sqs = new AmazonSQSClient();

            private readonly List<Task<ListObjectsResponse>> listTasks = new List<Task<ListObjectsResponse>>();
        
            
            public override async Task<Context> Execute(Context context)
            {
                var pngPath = $"{context.SceneProvisioningJobWorkspace.S3Key()}output/{context.SceneProvisioningJobId}/annotations/consolidated-annotation/output/";
                var jsonPath = $"{context.SceneProvisioningJobWorkspace.S3Key()}output/{context.SceneProvisioningJobId}/annotations/consolidated-annotation/consolidation-request/iteration-1/";
               

                listTasks.Add(s3.ListObjectsAsync(new ListObjectsRequest
                {
                    BucketName = context.SceneProvisioningJobWorkspace.S3Bucket(),
                    Prefix = pngPath
                }));
                listTasks.Add(s3.ListObjectsAsync(new ListObjectsRequest
                {
                    BucketName = context.SceneProvisioningJobWorkspace.S3Bucket(),
                    Prefix = jsonPath
                }));

                await Task.WhenAll(listTasks);

                var pngObject = listTasks[0].Result.S3Objects.Single();
                var jsonObject = listTasks[1].Result.S3Objects.Single();
                
                var pngResp = await s3.GetObjectAsync(new GetObjectRequest
                {
                    Key = pngObject.Key,
                    BucketName = context.SceneProvisioningJobWorkspace.S3Bucket(),
                });
                var jpgResp = await s3.GetObjectAsync(new GetObjectRequest
                {
                    BucketName = context.SceneImageLocation.S3Bucket(),
                    Key = context.SceneImageLocation.S3Key()
                });
                var jsonResp = await s3.GetObjectAsync(new GetObjectRequest
                {
                    BucketName = context.SceneProvisioningJobWorkspace.S3Bucket(),
                    Key = jsonObject.Key
                });

                var png = Image.Load(pngResp.ResponseStream, new PngDecoder());
                var image = Image.Load(jpgResp.ResponseStream);

                if (context.GenerateSceneBackground)
                {
                    Console.WriteLine("Making full mask PNG.");
                    var fullMask = new Image<Rgba32>(png.Width, png.Height);
                    for (var x = 0; x < png.Width; x++)
                    {
                        for (var y = 0; y < png.Height; y++)
                        {
                            var pixel = png.Frames[0][x, y];
                            if (!(pixel.R == 255 &&
                                  pixel.G == 255 &&
                                  pixel.B == 255 &&
                                  pixel.R == 255))
                            {
                                var x1 = x;
                                var y1 = y;
                                var pixelToCopy = image.Clone(i => i.Crop(new Rectangle(x1, y1, 1, 1)));

                                fullMask.Mutate(m => m.DrawImage(pixelToCopy, 1, new Point(x1, y1)));
                            }
                        }
                    }

                    using (var stream = new MemoryStream())
                    {
                        fullMask.Save(stream, new PngEncoder());
                        await s3.PutObjectAsync(new PutObjectRequest
                        {
                            BucketName = context.SceneProvisioningJobWorkspace.S3Bucket(),
                            Key = $"{context.SceneProvisioningJobWorkspace.S3Key()}full-mask.png",
                            ContentType = "image/png",
                            InputStream = stream
                        });
                    }

                    context.BackgroundImagePercentComplete = 10;
                    context.SceneBackgroundLocation = context.SceneProvisioningJobWorkspace + "scene-background.jpg";

                    await sqs.SendMessageAsync(new SendMessageRequest
                    {
                        QueueUrl = context.SceneBackgroundGenerationQueueUrl,
                        MessageBody = JsonConvert.SerializeObject(context)
                    });