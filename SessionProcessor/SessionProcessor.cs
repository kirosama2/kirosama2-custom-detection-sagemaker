
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