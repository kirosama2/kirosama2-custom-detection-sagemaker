
from threading import Thread, Event, Timer
import os  
import json  
import numpy as np  
import awscam  
import cv2  
import time  
import greengrasssdk 
import datetime
import boto3
import datetime



client = greengrasssdk.client('iot-data')
iotTopic = '$aws/things/{}/infer'.format(os.environ['AWS_IOT_THING_NAME'])
thingName = os.environ['AWS_IOT_THING_NAME'];

motionThreshold = 200
bucketName = 'bucket-name-here'

def load_config_values():
    try:
        ssm = boto3.client('ssm', region_name='us-east-1')
        response = ssm.get_parameters(
            Names=[
                '/Cameras/{cameraKey}/CameraBucket'.format(cameraKey=thingName),
                '/Cameras/{cameraKey}/MotionThreshold'.format(cameraKey=thingName)
            ],