# DEPLOYMENT BLOCKED - IAM Permission Insufficient

**Status:** ⛔ BLOCKED  
**Time:** 13:10 EST  
**Blocker:** `fortress-tools-deployer` IAM user lacks infrastructure creation permissions

---

## Problem

The deployment runbook requires creating new AWS infrastructure:
- CloudFormation stacks (LMA main stack with nested stacks)
- S3 buckets (for CloudFormation packaging)
- ECR repositories
- SQS queues (main + DLQ)
- RDS SQL Server instance
- ECS cluster
- IAM roles (task roles, execution roles)
- EC2 security groups
- VPC resources
- CloudWatch log groups

**Current IAM user:** `fortress-tools-deployer`  
**Current permissions:** Limited to ECR push and ECS service updates (for deploying to *existing* infrastructure)

---

## Evidence

### Attempted Actions & Results

1. **S3 bucket creation** - ❌ AccessDenied
   ```
   User: arn:aws:iam::742932328420:user/fortress-tools-deployer is not authorized 
   to perform: s3:CreateBucket
   ```

2. **S3 bucket listing** - ❌ AccessDenied
   ```
   not authorized to perform: s3:ListAllMyBuckets
   ```

3. **IAM policy inspection** - ❌ AccessDenied
   ```
   not authorized to perform: iam:GetUserPolicy
   not authorized to perform: iam:ListAttachedUserPolicies
   ```

4. **CloudFormation describe-stacks** - ✅ Works (read-only)

5. **STS get-caller-identity** - ✅ Works
   ```json
   {
     "UserId": "AIDA2Z6RXV7SAM4HFP7WX",
     "Account": "742932328420",
     "Arn": "arn:aws:iam::742932328420:user/fortress-tools-deployer"
   }
   ```

---

## LMA Stack Requirements

The LMA CloudFormation template (`lma-main.yaml`) requires:
- **SAM Transform** (`AWS::Serverless-2016-10-31`)
- **Nested stacks** (10+ child stacks referenced via S3 URLs)
- **S3 packaging** (via `aws cloudformation package` or `sam package`)

Template contains placeholders like:
- `<ARTIFACT_BUCKET_TOKEN>`
- `<ARTIFACT_PREFIX_TOKEN>`
- `<REGION_TOKEN>`
- `<VERSION_TOKEN>`

These must be resolved during packaging, which requires:
1. Creating/accessing an S3 bucket for artifacts
2. Uploading nested templates and Lambda code to S3
3. Rewriting template URLs to actual S3 locations
4. Deploying the packaged template

---

## Required Permissions (Missing)

To complete the deployment, the IAM user/role needs:

### CloudFormation
- `cloudformation:CreateStack`
- `cloudformation:DescribeStacks`
- `cloudformation:CreateChangeSet`
- `cloudformation:ExecuteChangeSet`
- `cloudformation:GetTemplateSummary`

### S3 (for CloudFormation packaging)
- `s3:CreateBucket`
- `s3:PutObject`
- `s3:GetObject`
- `s3:ListBucket`
- `s3:DeleteObject` (for cleanup)

### IAM (for creating task roles)
- `iam:CreateRole`
- `iam:PutRolePolicy`
- `iam:AttachRolePolicy`
- `iam:GetRole`
- `iam:PassRole`
- `iam:CreateServiceLinkedRole` (for ECS, RDS)

### EC2 (for VPC resources)
- `ec2:DescribeVpcs`
- `ec2:DescribeSubnets`
- `ec2:CreateSecurityGroup`
- `ec2:AuthorizeSecurityGroupIngress`
- `ec2:DescribeSecurityGroups`

### RDS
- `rds:CreateDBInstance`
- `rds:DescribeDBInstances`
- `rds:CreateDBSubnetGroup`
- `rds:AddTagsToResource`

### SQS
- `sqs:CreateQueue`
- `sqs:GetQueueUrl`
- `sqs:GetQueueAttributes`
- `sqs:SetQueueAttributes`

### ECS
- `ecs:CreateCluster`
- `ecs:RegisterTaskDefinition`
- `ecs:CreateService`
- `ecs:DescribeClusters`
- `ecs:DescribeServices`
- `ecs:DescribeTasks`

### ECR
- `ecr:CreateRepository`
- `ecr:DescribeRepositories`
- `ecr:PutImage`
- `ecr:InitiateLayerUpload`
- `ecr:UploadLayerPart`
- `ecr:CompleteLayerUpload`
- `ecr:GetAuthorizationToken`

### CloudWatch Logs
- `logs:CreateLogGroup`
- `logs:CreateLogStream`
- `logs:PutLogEvents`

### Additional (for LMA stack)
- `kinesis:*` (Kinesis Data Streams)
- `transcribe:*` (Amazon Transcribe)
- `appsync:*` (AWS AppSync)
- `dynamodb:*` (DynamoDB tables)
- `cognito-idp:*` (Cognito User Pools)
- `lambda:*` (Lambda functions)
- `bedrock:*` (Amazon Bedrock)

---

## Options

### Option 1: Create New IAM User (Recommended)
Create `refuge-meeting-deployer` IAM user with:
- Managed policy: `PowerUserAccess` (or custom policy with required permissions)
- Store credentials in `/home/fredw/.openclaw/workspace/meeting-assistant-aws/.env.deployer`

### Option 2: Expand fortress-tools-deployer Permissions
Attach additional policies to `fortress-tools-deployer`:
- Custom policy with all required permissions listed above
- Or managed policy like `PowerUserAccess`

⚠️ **Note:** This may violate the principle of least privilege if fortress-tools is only for existing infrastructure deployments.

### Option 3: Use AWS Administrator Access (Not Recommended for Automation)
Use root/admin credentials temporarily to unblock deployment, then create proper IAM user.

### Option 4: Manual AWS Console Deployment
Deploy LMA stack manually via AWS Console, then use `fortress-tools-deployer` for subsequent code deployments.

---

## Recommended Next Steps

1. **Create IAM user** with infrastructure provisioning permissions
   ```bash
   # Via AWS CLI with admin credentials
   aws iam create-user --user-name refuge-meeting-deployer
   
   aws iam attach-user-policy \
     --user-name refuge-meeting-deployer \
     --policy-arn arn:aws:iam::aws:policy/PowerUserAccess
   
   aws iam create-access-key --user-name refuge-meeting-deployer
   ```

2. **Store new credentials**
   ```bash
   # Create .env file
   cat > /home/fredw/.openclaw/workspace/meeting-assistant-aws/.env.deployer <<EOF
   AWS_ACCESS_KEY_ID=<new-access-key>
   AWS_SECRET_ACCESS_KEY=<new-secret-key>
   AWS_DEFAULT_REGION=us-east-1
   EOF
   ```

3. **Resume deployment** with new credentials
   ```bash
   source /home/fredw/.openclaw/workspace/meeting-assistant-aws/.env.deployer
   # Continue with runbook steps
   ```

---

## Alternative: Check for Existing Deployment Bucket

If an S3 bucket for CloudFormation deployments already exists:
```bash
# Try common naming patterns
aws s3 ls s3://refuge-cloudformation-artifacts --region us-east-1
aws s3 ls s3://lma-deployment-artifacts --region us-east-1
aws s3 ls s3://742932328420-cloudformation-us-east-1 --region us-east-1
```

If found, the bucket name can be used with `aws cloudformation package --s3-bucket <bucket-name>` (assuming write permissions exist).

---

## Time Impact

- **Time spent:** 4 minutes (investigation)
- **Time remaining:** 116 minutes
- **Blocker resolution time:** Depends on IAM user creation (5-10 minutes) or permission modification (2-5 minutes)

---

**Status:** Awaiting IAM permission resolution  
**Next:** Create/modify IAM user, then resume deployment from Part 1 Step 2
