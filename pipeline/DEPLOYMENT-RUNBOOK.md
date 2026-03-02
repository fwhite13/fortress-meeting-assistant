# Deployment Runbook: Meeting Assistant AWS Dev Environment

**Status:** Ready to execute (awaiting permissions)  
**Time required:** ~80 minutes  
**Prerequisites:** AWS permissions applied (see GRANT-PERMISSIONS-HOWTO.md)

---

## Pre-Flight Check

```bash
# Verify permissions (should succeed)
aws cloudformation describe-stacks --region us-east-1 --max-items 1
aws ecr describe-repositories --region us-east-1 --max-items 1
aws sqs list-queues --region us-east-1
```

If any command fails with "AccessDenied", permissions not yet applied.

---

## Part 1: Deploy LMA CloudFormation Stack (35 minutes)

### Step 1.1: Start Stack Deployment (< 1 minute)

```bash
cd /home/fredw/.openclaw/workspace/lma

aws cloudformation create-stack \
  --stack-name lma-dev \
  --template-body file://lma-main.yaml \
  --parameters file:///tmp/lma-deploy-params.json \
  --capabilities CAPABILITY_IAM CAPABILITY_NAMED_IAM CAPABILITY_AUTO_EXPAND \
  --region us-east-1
```

**Expected output:**
```json
{
    "StackId": "arn:aws:cloudformation:us-east-1:742932328420:stack/lma-dev/..."
}
```

---

### Step 1.2: Monitor Stack Creation (background, ~30 minutes)

**In terminal 1 (monitoring):**
```bash
# Watch stack events in real-time
aws cloudformation describe-stack-events \
  --stack-name lma-dev \
  --region us-east-1 \
  --max-items 10 \
  --query 'StackEvents[].[Timestamp,LogicalResourceId,ResourceStatus,ResourceStatusReason]' \
  --output table
```

**Or use wait command (blocks until complete):**
```bash
aws cloudformation wait stack-create-complete \
  --stack-name lma-dev \
  --region us-east-1
```

**While waiting, proceed to Part 2 in parallel →**

---

### Step 1.3: Capture Stack Outputs (when complete)

```bash
aws cloudformation describe-stacks \
  --stack-name lma-dev \
  --region us-east-1 \
  --query 'Stacks[0].Outputs' \
  > /home/fredw/.openclaw/workspace/meeting-assistant-aws/lma-stack-outputs.json

# Display outputs
cat /home/fredw/.openclaw/workspace/meeting-assistant-aws/lma-stack-outputs.json | jq '.'
```

**Expected outputs:**
- `KinesisDataStreamName` (e.g., `lma-dev-AudioStream`)
- `AppSyncGraphQLUrl` (e.g., `https://xxx.appsync-api.us-east-1.amazonaws.com/graphql`)
- `CognitoUserPoolId` (e.g., `us-east-1_xxxxxx`)
- `S3BucketName` (e.g., `lma-dev-audio-bucket-...`)
- `WebUIUrl` (e.g., `https://xxx.amplifyapp.com`)

---

## Part 2: Create AWS Dev Infrastructure (in parallel, ~50 minutes)

### Step 2.1: Create ECR Repositories (2 minutes)

**API repository:**
```bash
aws ecr create-repository \
  --repository-name refuge-meeting-assistant-api-dev \
  --region us-east-1 \
  --tags Key=Environment,Value=dev Key=Project,Value=refuge-meeting-assistant \
  --image-scanning-configuration scanOnPush=true

API_ECR_URI=$(aws ecr describe-repositories \
  --region us-east-1 \
  --repository-names refuge-meeting-assistant-api-dev \
  --query 'repositories[0].repositoryUri' \
  --output text)

echo "API ECR URI: $API_ECR_URI"
```

**VP Bot repository:**
```bash
aws ecr create-repository \
  --repository-name refuge-meeting-assistant-vpbot-dev \
  --region us-east-1 \
  --tags Key=Environment,Value=dev Key=Project,Value=refuge-meeting-assistant \
  --image-scanning-configuration scanOnPush=true

VPBOT_ECR_URI=$(aws ecr describe-repositories \
  --region us-east-1 \
  --repository-names refuge-meeting-assistant-vpbot-dev \
  --query 'repositories[0].repositoryUri' \
  --output text)

echo "VP Bot ECR URI: $VPBOT_ECR_URI"
```

---

### Step 2.2: Create SQS Queues (3 minutes)

**Dead letter queue (create first):**
```bash
aws sqs create-queue \
  --queue-name refuge-meeting-bot-commands-dev-dlq \
  --attributes MessageRetentionPeriod=1209600 \
  --region us-east-1

DLQ_URL=$(aws sqs get-queue-url \
  --queue-name refuge-meeting-bot-commands-dev-dlq \
  --region us-east-1 \
  --query 'QueueUrl' \
  --output text)

DLQ_ARN=$(aws sqs get-queue-attributes \
  --queue-url $DLQ_URL \
  --attribute-names QueueArn \
  --region us-east-1 \
  --query 'Attributes.QueueArn' \
  --output text)

echo "DLQ URL: $DLQ_URL"
echo "DLQ ARN: $DLQ_ARN"
```

**Main queue (with DLQ configured):**
```bash
aws sqs create-queue \
  --queue-name refuge-meeting-bot-commands-dev \
  --attributes \
    VisibilityTimeout=300,\
MessageRetentionPeriod=86400,\
RedrivePolicy="{\"deadLetterTargetArn\":\"$DLQ_ARN\",\"maxReceiveCount\":\"3\"}" \
  --region us-east-1

QUEUE_URL=$(aws sqs get-queue-url \
  --queue-name refuge-meeting-bot-commands-dev \
  --region us-east-1 \
  --query 'QueueUrl' \
  --output text)

echo "Main Queue URL: $QUEUE_URL"
```

---

### Step 2.3: Create RDS SQL Server Instance (20 minutes)

**Get default VPC and subnets:**
```bash
DEFAULT_VPC=$(aws ec2 describe-vpcs \
  --filters Name=isDefault,Values=true \
  --query 'Vpcs[0].VpcId' \
  --output text \
  --region us-east-1)

echo "Default VPC: $DEFAULT_VPC"

SUBNET_IDS=$(aws ec2 describe-subnets \
  --filters "Name=vpc-id,Values=$DEFAULT_VPC" \
  --query 'Subnets[*].SubnetId' \
  --output text \
  --region us-east-1)

echo "Subnets: $SUBNET_IDS"

# Convert space-separated to array
SUBNET_ARRAY=($SUBNET_IDS)
SUBNET_1=${SUBNET_ARRAY[0]}
SUBNET_2=${SUBNET_ARRAY[1]}
```

**Create DB subnet group:**
```bash
aws rds create-db-subnet-group \
  --db-subnet-group-name refuge-meeting-dev-subnets \
  --db-subnet-group-description "Subnets for Refuge Meeting Assistant dev RDS" \
  --subnet-ids $SUBNET_1 $SUBNET_2 \
  --tags Key=Environment,Value=dev Key=Project,Value=refuge-meeting-assistant \
  --region us-east-1
```

**Create security group:**
```bash
SG_ID=$(aws ec2 create-security-group \
  --group-name refuge-meeting-rds-dev \
  --description "Allow SQL Server access for Refuge Meeting Assistant dev" \
  --vpc-id $DEFAULT_VPC \
  --region us-east-1 \
  --query 'GroupId' \
  --output text)

echo "Security Group ID: $SG_ID"

aws ec2 authorize-security-group-ingress \
  --group-id $SG_ID \
  --protocol tcp \
  --port 1433 \
  --cidr 0.0.0.0/0 \
  --region us-east-1

aws ec2 create-tags \
  --resources $SG_ID \
  --tags Key=Name,Value=refuge-meeting-rds-dev Key=Environment,Value=dev \
  --region us-east-1
```

**Generate strong password:**
```bash
RDS_PASSWORD=$(openssl rand -base64 24 | tr -d "/@\"'\`\\")
echo "Generated RDS Password: $RDS_PASSWORD"
echo "(Save this — you'll need it!)"
```

**Create RDS instance:**
```bash
aws rds create-db-instance \
  --db-instance-identifier refuge-meeting-dev \
  --db-instance-class db.t3.micro \
  --engine sqlserver-ex \
  --master-username admin \
  --master-user-password "$RDS_PASSWORD" \
  --allocated-storage 20 \
  --storage-type gp3 \
  --vpc-security-group-ids $SG_ID \
  --db-subnet-group-name refuge-meeting-dev-subnets \
  --backup-retention-period 1 \
  --no-multi-az \
  --publicly-accessible \
  --tags Key=Environment,Value=dev Key=Project,Value=refuge-meeting-assistant \
  --region us-east-1
```

**Wait for availability (~15 minutes):**
```bash
echo "Waiting for RDS instance to become available (this takes ~15 minutes)..."
aws rds wait db-instance-available \
  --db-instance-identifier refuge-meeting-dev \
  --region us-east-1
```

**Capture endpoint:**
```bash
RDS_ENDPOINT=$(aws rds describe-db-instances \
  --db-instance-identifier refuge-meeting-dev \
  --query 'DBInstances[0].Endpoint.Address' \
  --output text \
  --region us-east-1)

echo "RDS Endpoint: $RDS_ENDPOINT"
```

---

### Step 2.4: Create ECS Cluster (1 minute)

```bash
aws ecs create-cluster \
  --cluster-name refuge-meeting-dev \
  --capacity-providers FARGATE FARGATE_SPOT \
  --default-capacity-provider-strategy capacityProvider=FARGATE,weight=1 \
  --tags key=Environment,value=dev key=Project,value=refuge-meeting-assistant \
  --region us-east-1

aws ecs describe-clusters \
  --clusters refuge-meeting-dev \
  --region us-east-1
```

---

### Step 2.5: Create IAM Roles (10 minutes)

**Create ECS task trust policy:**
```bash
cat > /tmp/ecs-task-trust-policy.json <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Service": "ecs-tasks.amazonaws.com"
      },
      "Action": "sts:AssumeRole"
    }
  ]
}
EOF
```

**API Task Role:**
```bash
aws iam create-role \
  --role-name RefugeMeetingApiTaskRole-dev \
  --assume-role-policy-document file:///tmp/ecs-task-trust-policy.json \
  --tags Key=Environment,Value=dev Key=Project,Value=refuge-meeting-assistant

API_TASK_ROLE_ARN=$(aws iam get-role \
  --role-name RefugeMeetingApiTaskRole-dev \
  --query 'Role.Arn' \
  --output text)

echo "API Task Role ARN: $API_TASK_ROLE_ARN"

# Get Queue ARN for policy
QUEUE_ARN=$(aws sqs get-queue-attributes \
  --queue-url $QUEUE_URL \
  --attribute-names QueueArn \
  --region us-east-1 \
  --query 'Attributes.QueueArn' \
  --output text)

aws iam put-role-policy \
  --role-name RefugeMeetingApiTaskRole-dev \
  --policy-name ApiTaskPolicy \
  --policy-document '{
    "Version": "2012-10-17",
    "Statement": [
      {
        "Effect": "Allow",
        "Action": [
          "sqs:SendMessage",
          "sqs:GetQueueUrl"
        ],
        "Resource": "'"$QUEUE_ARN"'"
      },
      {
        "Effect": "Allow",
        "Action": [
          "ecs:RunTask"
        ],
        "Resource": "*"
      },
      {
        "Effect": "Allow",
        "Action": [
          "iam:PassRole"
        ],
        "Resource": "*",
        "Condition": {
          "StringEquals": {
            "iam:PassedToService": "ecs-tasks.amazonaws.com"
          }
        }
      }
    ]
  }'
```

**VP Bot Task Role:**
```bash
aws iam create-role \
  --role-name RefugeMeetingVPBotTaskRole-dev \
  --assume-role-policy-document file:///tmp/ecs-task-trust-policy.json \
  --tags Key=Environment,Value=dev Key=Project,Value=refuge-meeting-assistant

VPBOT_TASK_ROLE_ARN=$(aws iam get-role \
  --role-name RefugeMeetingVPBotTaskRole-dev \
  --query 'Role.Arn' \
  --output text)

echo "VP Bot Task Role ARN: $VPBOT_TASK_ROLE_ARN"

aws iam put-role-policy \
  --role-name RefugeMeetingVPBotTaskRole-dev \
  --policy-name VPBotTaskPolicy \
  --policy-document '{
    "Version": "2012-10-17",
    "Statement": [
      {
        "Effect": "Allow",
        "Action": [
          "sqs:ReceiveMessage",
          "sqs:DeleteMessage",
          "sqs:GetQueueAttributes"
        ],
        "Resource": "'"$QUEUE_ARN"'"
      },
      {
        "Effect": "Allow",
        "Action": [
          "kinesis:PutRecord",
          "kinesis:PutRecords"
        ],
        "Resource": "arn:aws:kinesis:us-east-1:*:stream/*"
      },
      {
        "Effect": "Allow",
        "Action": [
          "s3:PutObject"
        ],
        "Resource": "arn:aws:s3:::*/*"
      }
    ]
  }'
```

**ECS Task Execution Role:**
```bash
# Check if role already exists (AWS managed)
aws iam get-role --role-name ecsTaskExecutionRole 2>&1 > /dev/null

if [ $? -ne 0 ]; then
  echo "Creating ecsTaskExecutionRole..."
  aws iam create-role \
    --role-name ecsTaskExecutionRole \
    --assume-role-policy-document file:///tmp/ecs-task-trust-policy.json

  aws iam attach-role-policy \
    --role-name ecsTaskExecutionRole \
    --policy-arn arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy
else
  echo "ecsTaskExecutionRole already exists"
fi

EXEC_ROLE_ARN=$(aws iam get-role \
  --role-name ecsTaskExecutionRole \
  --query 'Role.Arn' \
  --output text)

echo "Execution Role ARN: $EXEC_ROLE_ARN"
```

---

## Part 3: Generate Configuration Files (5 minutes)

### Step 3.1: Extract LMA Stack Outputs

```bash
cd /home/fredw/.openclaw/workspace/meeting-assistant-aws

# Parse outputs into variables
LMA_KINESIS_STREAM=$(jq -r '.[] | select(.OutputKey=="KinesisDataStreamName") | .OutputValue' lma-stack-outputs.json)
LMA_APPSYNC_URL=$(jq -r '.[] | select(.OutputKey=="AppSyncGraphQLUrl") | .OutputValue' lma-stack-outputs.json)
LMA_COGNITO_POOL_ID=$(jq -r '.[] | select(.OutputKey=="CognitoUserPoolId") | .OutputValue' lma-stack-outputs.json)
LMA_S3_BUCKET=$(jq -r '.[] | select(.OutputKey=="S3BucketName") | .OutputValue' lma-stack-outputs.json)
LMA_WEB_UI=$(jq -r '.[] | select(.OutputKey=="WebUIUrl") | .OutputValue' lma-stack-outputs.json)

echo "LMA Kinesis Stream: $LMA_KINESIS_STREAM"
echo "LMA AppSync URL: $LMA_APPSYNC_URL"
echo "LMA Cognito Pool: $LMA_COGNITO_POOL_ID"
echo "LMA S3 Bucket: $LMA_S3_BUCKET"
echo "LMA Web UI: $LMA_WEB_UI"
```

---

### Step 3.2: Write .env.aws-dev

```bash
cat > .env.aws-dev <<EOF
# AWS Region
AWS_REGION=us-east-1
AWS_ACCOUNT_ID=742932328420

# LMA Stack Outputs (CloudFormation)
LMA_KINESIS_STREAM=$LMA_KINESIS_STREAM
LMA_APPSYNC_URL=$LMA_APPSYNC_URL
LMA_COGNITO_POOL_ID=$LMA_COGNITO_POOL_ID
LMA_S3_BUCKET=$LMA_S3_BUCKET
LMA_WEB_UI=$LMA_WEB_UI

# ECR Repositories
API_ECR_URI=$API_ECR_URI
VPBOT_ECR_URI=$VPBOT_ECR_URI

# SQS Queues
SQS_QUEUE_URL=$QUEUE_URL
SQS_DLQ_URL=$DLQ_URL

# RDS SQL Server
RDS_ENDPOINT=$RDS_ENDPOINT
RDS_PORT=1433
RDS_DATABASE=refuge_meeting_dev
RDS_USERNAME=admin
RDS_PASSWORD=$RDS_PASSWORD

# ECS Cluster
ECS_CLUSTER=refuge-meeting-dev

# IAM Roles
API_TASK_ROLE_ARN=$API_TASK_ROLE_ARN
VPBOT_TASK_ROLE_ARN=$VPBOT_TASK_ROLE_ARN
ECS_EXECUTION_ROLE_ARN=$EXEC_ROLE_ARN

# Deployment Info
DEPLOYED_AT=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
DEPLOYED_BY=DevOps-Rhodey
EOF

cat .env.aws-dev
```

---

### Step 3.3: Update appsettings.json (Optional)

```bash
# Backup existing appsettings
cp src/RefugeMeetingAssistant.Api/appsettings.json \
   src/RefugeMeetingAssistant.Api/appsettings.json.backup

# Update connection string (manual edit or jq)
# TODO: Add jq command to inject RDS connection string
```

---

## Part 4: Verification & Summary (5 minutes)

### Verify All Resources Exist

```bash
echo "=== ECR Repositories ==="
aws ecr describe-repositories \
  --region us-east-1 \
  --repository-names refuge-meeting-assistant-api-dev refuge-meeting-assistant-vpbot-dev \
  --query 'repositories[*].[repositoryName,repositoryUri]' \
  --output table

echo ""
echo "=== SQS Queues ==="
aws sqs list-queues --region us-east-1 --queue-name-prefix refuge-meeting

echo ""
echo "=== RDS Instance ==="
aws rds describe-db-instances \
  --db-instance-identifier refuge-meeting-dev \
  --region us-east-1 \
  --query 'DBInstances[0].[DBInstanceIdentifier,DBInstanceStatus,Endpoint.Address]' \
  --output table

echo ""
echo "=== ECS Cluster ==="
aws ecs describe-clusters \
  --clusters refuge-meeting-dev \
  --region us-east-1 \
  --query 'clusters[0].[clusterName,status,registeredContainerInstancesCount]' \
  --output table

echo ""
echo "=== IAM Roles ==="
aws iam list-roles \
  --query 'Roles[?starts_with(RoleName, `RefugeMeeting`)].RoleName' \
  --output table

echo ""
echo "=== LMA CloudFormation Stack ==="
aws cloudformation describe-stacks \
  --stack-name lma-dev \
  --region us-east-1 \
  --query 'Stacks[0].[StackName,StackStatus,CreationTime]' \
  --output table
```

---

## Success Criteria Checklist

- [ ] LMA CloudFormation stack status: `CREATE_COMPLETE`
- [ ] ECR repositories created: `refuge-meeting-assistant-api-dev`, `refuge-meeting-assistant-vpbot-dev`
- [ ] SQS queues created: `refuge-meeting-bot-commands-dev`, `refuge-meeting-bot-commands-dev-dlq`
- [ ] RDS instance status: `available`
- [ ] RDS endpoint accessible (check security group allows 1433)
- [ ] ECS cluster status: `ACTIVE`
- [ ] IAM roles created: `RefugeMeetingApiTaskRole-dev`, `RefugeMeetingVPBotTaskRole-dev`, `ecsTaskExecutionRole`
- [ ] `.env.aws-dev` file contains all values
- [ ] `lma-stack-outputs.json` file exists

---

## Next Steps After Deployment

1. **Build and push Docker images:**
   - Build .NET API image → push to API ECR
   - Build VP Bot image → push to VP Bot ECR

2. **Create ECS Task Definitions:**
   - API task definition
   - VP Bot task definition

3. **Create ECS Services:**
   - API service (always-on, 1-2 tasks)
   - VP Bot service (on-demand, scales to zero)

4. **Test end-to-end:**
   - API health check
   - Submit meeting join request
   - Verify bot joins Teams meeting
   - Verify audio streams to Kinesis
   - Verify transcript appears in LMA

---

## Rollback Procedure

**If deployment fails and you need to start over:**

```bash
# Delete CloudFormation stack
aws cloudformation delete-stack --stack-name lma-dev --region us-east-1
aws cloudformation wait stack-delete-complete --stack-name lma-dev --region us-east-1

# Delete ECR repositories
aws ecr delete-repository --repository-name refuge-meeting-assistant-api-dev --region us-east-1 --force
aws ecr delete-repository --repository-name refuge-meeting-assistant-vpbot-dev --region us-east-1 --force

# Delete SQS queues
aws sqs delete-queue --queue-url $QUEUE_URL --region us-east-1
aws sqs delete-queue --queue-url $DLQ_URL --region us-east-1

# Delete RDS instance (skip final snapshot for dev)
aws rds delete-db-instance --db-instance-identifier refuge-meeting-dev --skip-final-snapshot --region us-east-1

# Delete ECS cluster
aws ecs delete-cluster --cluster refuge-meeting-dev --region us-east-1

# Delete IAM roles
aws iam delete-role-policy --role-name RefugeMeetingApiTaskRole-dev --policy-name ApiTaskPolicy
aws iam delete-role --role-name RefugeMeetingApiTaskRole-dev
aws iam delete-role-policy --role-name RefugeMeetingVPBotTaskRole-dev --policy-name VPBotTaskPolicy
aws iam delete-role --role-name RefugeMeetingVPBotTaskRole-dev

# Delete security group
aws ec2 delete-security-group --group-id $SG_ID --region us-east-1

# Delete DB subnet group
aws rds delete-db-subnet-group --db-subnet-group-name refuge-meeting-dev-subnets --region us-east-1
```

---

**Runbook ready. Awaiting permissions to execute.**
