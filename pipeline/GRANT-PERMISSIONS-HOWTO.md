# Quick Fix: Grant AWS Permissions for LMA Deployment

**Time required:** 5 minutes  
**Who can do this:** AWS account administrator or anyone with IAM policy management permissions

---

## The Problem

The `openclaw-bedrock` IAM user only has Bedrock API access. It needs additional permissions to deploy the Meeting Assistant infrastructure (CloudFormation, ECR, SQS, RDS, ECS, etc.).

---

## The Solution (Pick One)

### Option A: Via AWS Console (Easiest)

1. **Go to IAM Console:**  
   https://console.aws.amazon.com/iam/

2. **Navigate to Users:**  
   Click "Users" in left sidebar → Click "openclaw-bedrock"

3. **Add Inline Policy:**
   - Click "Add permissions" → "Create inline policy"
   - Click "JSON" tab
   - Copy the entire contents of `RefugeMeetingAssistantDeploymentPolicy.json` (in this directory)
   - Paste into the JSON editor
   - Click "Next"
   - Policy name: `RefugeMeetingAssistantDeployment`
   - Click "Create policy"

4. **Done!**  
   Re-delegate deployment task to DevOps agent.

---

### Option B: Via AWS CLI (Fastest)

```bash
aws iam put-user-policy \
  --user-name openclaw-bedrock \
  --policy-name RefugeMeetingAssistantDeployment \
  --policy-document file://$(pwd)/RefugeMeetingAssistantDeploymentPolicy.json
```

**Verify it worked:**
```bash
aws iam get-user-policy \
  --user-name openclaw-bedrock \
  --policy-name RefugeMeetingAssistantDeployment
```

---

### Option C: Use Different Credentials (Alternative)

If you have AWS credentials with broader permissions, export them temporarily:

```bash
export AWS_ACCESS_KEY_ID=<your-access-key>
export AWS_SECRET_ACCESS_KEY=<your-secret-key>
export AWS_SESSION_TOKEN=<if-using-temporary-credentials>
```

Then re-run the deployment.

---

## After Applying Permissions

**Resume deployment immediately:**

1. Re-delegate to DevOps agent: "Deploy Meeting Assistant AWS dev environment"
2. Deployment will complete in ~80 minutes:
   - LMA CloudFormation: 35 min
   - RDS SQL Server: 20 min (parallel with LMA)
   - ECR, SQS, ECS, IAM: 10 min (parallel)
   - Config generation: 5 min

---

## What This Policy Allows

The policy grants permissions for:
- ✅ CloudFormation (stack deployment)
- ✅ ECR (container registries)
- ✅ SQS (message queues)
- ✅ RDS (SQL Server database)
- ✅ ECS (Fargate container hosting)
- ✅ IAM (service roles)
- ✅ VPC/EC2 (networking, security groups)
- ✅ S3 (audio storage)
- ✅ Kinesis (streaming)
- ✅ Transcribe (speech-to-text)
- ✅ Cognito (authentication)
- ✅ AppSync + DynamoDB (GraphQL API)
- ✅ Lambda (processing functions)
- ✅ CloudWatch (logging)
- ✅ Amplify (web UI hosting)
- ✅ Application Load Balancer (API routing)

**Scope:** Limited to Meeting Assistant resources (prefixed with `refuge-`, `lma-`, `RefugeMeeting*`)

---

## Policy Location

**File:** `/home/fredw/.openclaw/workspace/meeting-assistant-aws/pipeline/RefugeMeetingAssistantDeploymentPolicy.json`

---

**Once applied, deployment can complete TODAY as originally planned.** ⚡
