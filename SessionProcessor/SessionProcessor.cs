
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