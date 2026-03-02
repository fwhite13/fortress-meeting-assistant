#!/bin/bash
# Deploy ECS API Service
# Requires: RDS endpoint, Docker images pushed to ECR, IAM roles created

set -e

source /home/fredw/.openclaw/workspace/ai/projects/fortress_tools/.env.deployer
source /home/fredw/.openclaw/workspace/meeting-assistant-aws/.env.aws-dev

echo "📋 Deploying ECS API Service..."

# Get default VPC and subnets
DEFAULT_VPC=$(aws ec2 describe-vpcs --filters Name=isDefault,Values=true --region us-east-1 --query 'Vpcs[0].VpcId' --output text)
SUBNET_IDS=$(aws ec2 describe-subnets --filters Name=vpc-id,Values=$DEFAULT_VPC --region us-east-1 --query 'Subnets[*].SubnetId' --output text)
SUBNET_ARRAY=($SUBNET_IDS)

# Create ECS security group (if not exists)
ECS_SG_ID=$(aws ec2 create-security-group \
  --group-name refuge-meeting-ecs-dev \
  --description "HTTP access for ECS tasks" \
  --vpc-id $DEFAULT_VPC \
  --region us-east-1 \
  --query 'GroupId' \
  --output text 2>&1) || \
ECS_SG_ID=$(aws ec2 describe-security-groups \
  --filters Name=group-name,Values=refuge-meeting-ecs-dev \
  --query 'SecurityGroups[0].GroupId' \
  --output text \
  --region us-east-1)

echo "Security Group: $ECS_SG_ID"

# Open port 5000
aws ec2 authorize-security-group-ingress \
  --group-id $ECS_SG_ID \
  --protocol tcp \
  --port 5000 \
  --cidr 0.0.0.0/0 \
  --region us-east-1 2>/dev/null || echo "Port 5000 ingress rule already exists"

# Create CloudWatch log group
aws logs create-log-group \
  --log-group-name /ecs/refuge-meeting-api-dev \
  --region us-east-1 2>/dev/null || echo "Log group already exists"

# Build task definition
API_ECR_URI="742932328420.dkr.ecr.us-east-1.amazonaws.com/refuge-meeting-assistant-api-dev"

cat > /tmp/api-task-def.json <<EOF
{
  "family": "refuge-meeting-api-dev",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "cpu": "512",
  "memory": "1024",
  "taskRoleArn": "$API_TASK_ROLE_ARN",
  "executionRoleArn": "$ECS_EXEC_ROLE_ARN",
  "containerDefinitions": [{
    "name": "api",
    "image": "$API_ECR_URI:latest",
    "portMappings": [{"containerPort": 5000, "protocol": "tcp"}],
    "environment": [
      {"name": "ASPNETCORE_ENVIRONMENT", "value": "Development"},
      {"name": "ConnectionStrings__DefaultConnection", "value": "Server=$RDS_ENDPOINT;Database=refuge_meeting_dev;User Id=admin;Password=$RDS_PASSWORD;TrustServerCertificate=True;"},
      {"name": "AWS__Region", "value": "us-east-1"},
      {"name": "AWS__SQS__BotCommandsQueueUrl", "value": "$SQS_QUEUE_URL"}
    ],
    "logConfiguration": {
      "logDriver": "awslogs",
      "options": {
        "awslogs-group": "/ecs/refuge-meeting-api-dev",
        "awslogs-region": "us-east-1",
        "awslogs-stream-prefix": "api"
      }
    },
    "healthCheck": {
      "command": ["CMD-SHELL", "curl -f http://localhost:5000/api/health || exit 1"],
      "interval": 30,
      "timeout": 5,
      "retries": 3,
      "startPeriod": 60
    }
  }]
}
EOF

echo "✅ Task definition created"

# Register task definition
TASK_DEF_ARN=$(aws ecs register-task-definition \
  --cli-input-json file:///tmp/api-task-def.json \
  --region us-east-1 \
  --query 'taskDefinition.taskDefinitionArn' \
  --output text)

echo "✅ Task definition registered: $TASK_DEF_ARN"

# Create or update service
SERVICE_EXISTS=$(aws ecs describe-services \
  --cluster refuge-meeting-dev \
  --services api-dev \
  --region us-east-1 \
  --query 'services[0].status' \
  --output text 2>/dev/null)

if [ "$SERVICE_EXISTS" == "ACTIVE" ]; then
  echo "Service exists, updating..."
  aws ecs update-service \
    --cluster refuge-meeting-dev \
    --service api-dev \
    --task-definition refuge-meeting-api-dev \
    --desired-count 1 \
    --region us-east-1 \
    --query 'service.serviceArn' \
    --output text
else
  echo "Creating new service..."
  aws ecs create-service \
    --cluster refuge-meeting-dev \
    --service-name api-dev \
    --task-definition refuge-meeting-api-dev \
    --desired-count 1 \
    --launch-type FARGATE \
    --network-configuration "awsvpcConfiguration={subnets=[${SUBNET_ARRAY[0]},${SUBNET_ARRAY[1]}],securityGroups=[$ECS_SG_ID],assignPublicIp=ENABLED}" \
    --region us-east-1 \
    --query 'service.serviceArn' \
    --output text
fi

echo "✅ ECS API service deployed"

# Wait for task to start
echo "⏱️ Waiting for task to start..."
sleep 45

# Get task public IP
TASK_ARN=$(aws ecs list-tasks \
  --cluster refuge-meeting-dev \
  --service-name api-dev \
  --region us-east-1 \
  --query 'taskArns[0]' \
  --output text)

if [ -n "$TASK_ARN" ] && [ "$TASK_ARN" != "None" ]; then
  ENI_ID=$(aws ecs describe-tasks \
    --cluster refuge-meeting-dev \
    --tasks $TASK_ARN \
    --region us-east-1 \
    --query 'tasks[0].attachments[0].details[?name==`networkInterfaceId`].value' \
    --output text)
  
  PUBLIC_IP=$(aws ec2 describe-network-interfaces \
    --network-interface-ids $ENI_ID \
    --region us-east-1 \
    --query 'NetworkInterfaces[0].Association.PublicIp' \
    --output text)
  
  echo "API_PUBLIC_ENDPOINT=http://$PUBLIC_IP:5000" >> /home/fredw/.openclaw/workspace/meeting-assistant-aws/.env.aws-dev
  echo "✅ API accessible at: http://$PUBLIC_IP:5000/api/health"
else
  echo "⚠️ Task not yet started, check status manually"
fi
