using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon;
using Amazon.CloudWatch.Model;
using Amazon.Lambda.Core;
using DotStep.Core;
using ModelBuilder.StateMachines;

namespace Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            AWSConfigs.AWSRegion = "us-east-1";
            var program = new Program();
    