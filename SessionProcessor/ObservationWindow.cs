using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CloudWatch.Model;

namespace SessionProcessor
{
    public class ObservationWindow
    {

        private readonly MetricDataResult metricData;

        public Observ