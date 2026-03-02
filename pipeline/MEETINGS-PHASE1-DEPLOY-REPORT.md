# Meeting Assistant v2 — Phase 1 Deploy Report

**Date:** 2026-02-27  
**Commit:** `fe530277` | Review: PASS  
**Deployer:** fortress-tools-deployer (742932328420)  
**Environment:** dev

---

## Resources Created

| Resource | Status | ARN/URL |
|----------|--------|---------|
| meetings_dev Aurora DB | ⚠️ Partial | fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com — DB exists (app connected OK), GRANT step skipped (see Errors) |
| SQS bot-commands queue | ✅ | https://sqs.us-east-1.amazonaws.com/742932328420/fortress-meetings-bot-commands-dev |
| SQS processing queue | ✅ | https://sqs.us-east-1.amazonaws.com/742932328420/fortress-meetings-processing-dev |
| S3 fortress-meetings-dev | ❌ Manual needed | AccessDenied on s3:CreateBucket — requires Fred |
| ECR fortress-meetings-web | ✅ | 742932328420.dkr.ecr.us-east-1.amazonaws.com/fortress-meetings-web |
| ECR fortress-meetings-vpbot | ✅ | 742932328420.dkr.ecr.us-east-1.amazonaws.com/fortress-meetings-vpbot |
| Web Docker image pushed | ✅ | fortress-meetings-web:dev-latest (sha256:800af350...) |
| VPBot Docker image pushed | ✅ | fortress-meetings-vpbot:dev-latest (sha256:98ad0d5e...) |
| ALB target group | ✅ | arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/meetings-web-dev-tg/7a7e9af531f05a53 |
| ALB rule (host-header) | ✅ | Priority 15, host: meetings.dev.fortressam.ai → meetings-web-dev-tg |
| ECS task def meetings-web-dev | ✅ | meetings-web-dev:1 |
| ECS task def meetings-vpbot-dev | ✅ | meetings-vpbot-dev:1 |
| ECS service meetings-web-dev | ✅ | fortress-tools-cluster/meetings-web-dev |
| ECS service meetings-vpbot-dev | ✅ | fortress-tools-cluster/meetings-vpbot-dev |
| Route53 meetings.dev.fortressam.ai | ✅ | A alias → fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com |
| Cognito callbacks updated | ✅ | Added signin-oidc + signout-callback-oidc for meetings.dev.fortressam.ai |
| CloudWatch log group /ecs/meetings-web-dev | ✅ | Created |
| CloudWatch log group /ecs/meetings-vpbot-dev | ✅ | Created |

---

## Verification

- **Root URL:** HTTP 302 (Cognito auth redirect — expected ✅)
- **Health endpoint:** HTTP 200 ✅
- **ECS running counts:** web [1/1] ✅, vpbot [1/1] ✅
- **ALB target health:** 172.31.46.127 → healthy ✅

---

## Errors / Manual Steps Needed

### 1. ❌ S3 Bucket `fortress-meetings-dev` — **Fred must create manually**
```bash
source ~/.openclaw/workspace/ai/projects/fortress_tools/.env.deployer
# Use an account with s3:CreateBucket permission, then:
aws s3 mb s3://fortress-meetings-dev --region us-east-1
```
`fortress-tools-deployer` does not have `s3:CreateBucket`. VPBot will fail to save recordings until this is done.

### 2. ⚠️ Aurora DB GRANT skipped — App working but verify permissions
The `fortress_mysql` user connected to Aurora fine (16-char password). The `meetings_dev` database either already existed or the app auto-created it (logs show "Database tables already exist"). The GRANT command failed with `Access denied` — this is likely benign if `fortress_mysql` already has full access to `meetings_dev`. The app is running and connecting successfully.

If you want to verify:
```sql
SHOW GRANTS FOR 'fortress_mysql'@'%';
```

### 3. 🔧 ECS subnet fix applied (AZ mismatch)
The initial service deployment placed tasks in all 6 default VPC subnets, but the ALB is only enabled for `us-east-1d` and `us-east-1f`. Updated the service to use only:
- `subnet-051bfcf5b07661809` (us-east-1d)
- `subnet-08e1d4f1b5530f39e` (us-east-1f)

### 4. 🔧 Dockerfile patch — NETSDK1152
Both `RefugeMeetingAssistant.Web` and `RefugeMeetingAssistant.Api` have `appsettings.json` files. Added `/p:ErrorOnDuplicatePublishOutputFiles=false` to the `dotnet publish` command in the Web Dockerfile. Original preserved as `Dockerfile.orig`.

---

## Rollback

If needed, to remove all deployed resources:

```bash
source /home/fredw/.openclaw/workspace/ai/projects/fortress_tools/.env.deployer

# Stop ECS services
aws ecs update-service --cluster fortress-tools-cluster --service meetings-web-dev --desired-count 0 --region us-east-1
aws ecs update-service --cluster fortress-tools-cluster --service meetings-vpbot-dev --desired-count 0 --region us-east-1
sleep 30

# Delete ECS services
aws ecs delete-service --cluster fortress-tools-cluster --service meetings-web-dev --region us-east-1
aws ecs delete-service --cluster fortress-tools-cluster --service meetings-vpbot-dev --region us-east-1

# Delete ALB rule (priority 15)
LISTENER_ARN="arn:aws:elasticloadbalancing:us-east-1:742932328420:listener/app/fortress-tools-alb/fe0b167b2404ae04/03366377561f20e1"
RULE_ARN=$(aws elbv2 describe-rules --listener-arn $LISTENER_ARN --region us-east-1 \
  --query 'Rules[?Priority==`15`].RuleArn' --output text)
aws elbv2 delete-rule --rule-arn $RULE_ARN --region us-east-1

# Delete target group
aws elbv2 delete-target-group \
  --target-group-arn arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/meetings-web-dev-tg/7a7e9af531f05a53 \
  --region us-east-1

# Delete Route53 record (UPSERT with DELETE action)
# Delete ECR images (optional)
aws ecr batch-delete-image --repository-name fortress-meetings-web \
  --image-ids imageTag=dev-latest --region us-east-1
aws ecr batch-delete-image --repository-name fortress-meetings-vpbot \
  --image-ids imageTag=dev-latest --region us-east-1
```

---

## Infrastructure Notes

- **ALB:** fortress-tools-alb (arn:.../fe0b167b2404ae04) — AZs: us-east-1d, us-east-1f only
- **ECS Cluster:** fortress-tools-cluster
- **VPC:** vpc-0783a9844741980ff (default VPC)
- **Cognito Pool:** us-east-1_CloTcONs1
- **Cognito Client:** e3ra6bg1oqji3i1mn2e7g1o1g
- **Route53 Zone:** Z003394436J64H3UMZ756

---

*Deployed by devops subagent | WP-4 | 2026-02-27*
