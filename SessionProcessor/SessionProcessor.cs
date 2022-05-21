
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.Runtime.Internal;

namespace SessionProcessor
{
    public delegate void MetricsLoaded(int numberOfMetrics);

    public delegate void ObservationsCreated(int numberOfObservations, DateTime firstObservation,
        DateTime lastObservation);

    public delegate void SessionsDiscovered(int numberOfSessions);

    public class SessionProcessor
    {
        private readonly ISessionStore sessionStore;
        public List<Session> Sessions;

        public SessionProcessor(string cameraKey, List<string> classNames, string predictionEndpointName,
            double objectMovedDetectionThreshold = 0.25)
        {
            CameraKey = cameraKey;
            ClassNames = classNames;
            PredictionEndpointName = predictionEndpointName;
            ObjectMovedDetectionThreshold = objectMovedDetectionThreshold;
            sessionStore = new DynamoDBSessionStore();
        }

        public string CameraKey { get; }
        public List<string> ClassNames { get; }
        public string PredictionEndpointName { get; }

        public ObservationWindow PersonObservation { get; set; }
        public List<ObservationWindow> ClassObservations { get; set; }

        public List<MetricDataResult> MetricData { get; private set; }

        public double ObjectMovedDetectionThreshold { get; set; }
        public event MetricsLoaded MetricsLoaded;
        public event ObservationsCreated ObservationsCreated;
        public event SessionsDiscovered SessionsDiscovered;

        public async Task StoreSessions(bool storeCompletedSessionsWithNoItems = true)
        {
            var storageTasks = new List<Task>();
            foreach (var session in Sessions)
                switch (session.Status)
                {
                    case "COMPLETED":
                        if (session.Items.Count > 0)
                            storageTasks.Add(sessionStore.PutSession(session));
                        else if (storeCompletedSessionsWithNoItems)
                            storageTasks.Add(sessionStore.PutSession(session));
                        else storageTasks.Add(sessionStore.DeleteSession(session.Id));
                        break;
                    default:
                        storageTasks.Add(sessionStore.PutSession(session));
                        break;
                }
            await Task.WhenAll(storageTasks);
        }

        public async Task LoadMetrics(int minutes = 15, int period = 10)
        {
            var cloudWatch = new AmazonCloudWatchClient();

            var getMetricRequest = new GetMetricDataRequest
            {
                StartTimeUtc = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(minutes)),
                EndTimeUtc = DateTime.UtcNow,
                MetricDataQueries = new AutoConstructedList<MetricDataQuery>
                {
                    new MetricDataQuery
                    {
                        Id = "Person".ToLower(),
                        MetricStat = new MetricStat
                        {
                            Metric = new Metric
                            {
                                Namespace = "Cameras",
                                MetricName = "Confidence",
                                Dimensions = new AutoConstructedList<Dimension>
                                {
                                    new Dimension
                                    {
                                        Name = "CameraKey",
                                        Value = CameraKey
                                    },
                                    new Dimension
                                    {
                                        Name = "Label",
                                        Value = "Person"
                                    },
                                    new Dimension
                                    {
                                        Name = "Source",
                                        Value = "Rekognition"
                                    }
                                }
                            },
                            Period = period,
                            Stat = "Maximum",
                            Unit = StandardUnit.Percent
                        },
                        ReturnData = true
                    }
                }
            };

            foreach (var className in ClassNames)
                getMetricRequest.MetricDataQueries.Add(new MetricDataQuery
                {
                    Id = className.ToLower(),
                    MetricStat = new MetricStat
                    {
                        Metric = new Metric
                        {
                            Namespace = "Cameras",
                            MetricName = "Confidence",
                            Dimensions = new AutoConstructedList<Dimension>
                            {
                                new Dimension
                                {
                                    Name = "CameraKey",
                                    Value = CameraKey
                                },
                                new Dimension
                                {
                                    Name = "Label",
                                    Value = className
                                },
                                new Dimension
                                {
                                    Name = "Source",
                                    Value = PredictionEndpointName