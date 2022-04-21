using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CloudWatch.Model;

namespace SessionProcessor
{
    public class ObservationWindow
    {

        private readonly MetricDataResult metricData;

        public ObservationWindow(MetricDataResult metricData)
        {
            this.metricData = metricData;

            var values = new Dictionary<DateTime, double>();
            var i = metricData.Timestamps.Count - 1;
            foreach (v