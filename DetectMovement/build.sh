#!/bin/bash
echo "Installing required modules."
sudo pip install -r requirements.txt -t .
echo "Removing test folders to low