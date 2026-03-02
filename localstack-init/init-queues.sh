#!/bin/bash
# LocalStack initialization script
# Creates SQS queues for the meeting assistant

echo "Creating SQS queues..."

awslocal sqs create-queue --queue-name refuge-meeting-bot-commands
awslocal sqs create-queue --queue-name refuge-meeting-processing
awslocal sqs create-queue --queue-name refuge-meeting-bot-commands-dlq

# Create S3 bucket
echo "Creating S3 bucket..."
awslocal s3 mb s3://refuge-meeting-assistant

echo "LocalStack initialization complete!"
awslocal sqs list-queues
awslocal s3 ls
