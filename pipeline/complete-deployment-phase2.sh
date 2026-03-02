#!/bin/bash
# Complete AWS Dev Deployment — Phase 2
# Run this after Fred's meeting (after 3:00 PM)
# Requires: RDS instance to be available

set -e

echo "=================================================="
echo "AWS Dev Deployment — Phase 2 Completion"
echo "Started: $(date +%H:%M)"
echo "=================================================="
echo

# Source credentials
source /home/fredw/.openclaw/workspace/ai/projects/fortress_tools/.env.deployer
source /home/fredw/.openclaw/workspace/meeting-assistant-aws/.env.aws-dev

# Step 1: Wait for RDS
echo "📊 Step 1: Waiting for RDS to become available..."
aws rds wait db-instance-available --db-instance-identifier refuge-meeting-dev --region us-east-1
echo "✅ RDS is available!"
echo

# Step 2: Capture RDS endpoint
echo "📊 Step 2: Capturing RDS endpoint..."
RDS_ENDPOINT=$(aws rds describe-db-instances \
  --db-instance-identifier refuge-meeting-dev \
  --region us-east-1 \
  --query 'DBInstances[0].Endpoint.Address' \
  --output text)

echo "RDS_ENDPOINT=$RDS_ENDPOINT" >> /home/fredw/.openclaw/workspace/meeting-assistant-aws/.env.aws-dev
echo "✅ RDS endpoint: $RDS_ENDPOINT"
echo

# Step 3: Run database migrations
echo "📊 Step 3: Running database migrations..."
cd /home/fredw/.openclaw/workspace/meeting-assistant-aws/src/RefugeMeetingAssistant.Api

DB_CONN="Server=$RDS_ENDPOINT;Database=refuge_meeting_dev;User Id=admin;Password=$RDS_PASSWORD;TrustServerCertificate=True;"

dotnet ef database update --connection "$DB_CONN"
echo "✅ Database schema deployed"
echo

# Step 4: Deploy ECS API service
echo "📊 Step 4: Deploying ECS API service..."
/home/fredw/.openclaw/workspace/meeting-assistant-aws/pipeline/deploy-ecs-api.sh
echo

# Step 5: Test API
echo "📊 Step 5: Testing API health endpoint..."
sleep 10
source /home/fredw/.openclaw/workspace/meeting-assistant-aws/.env.aws-dev

if [ -n "$API_PUBLIC_ENDPOINT" ]; then
  echo "Testing: $API_PUBLIC_ENDPOINT/api/health"
  curl -f "$API_PUBLIC_ENDPOINT/api/health" && echo && echo "✅ API is healthy!" || echo "⚠️ API health check failed"
else
  echo "⚠️ API_PUBLIC_ENDPOINT not set, skipping health check"
fi

echo
echo "=================================================="
echo "✅ Phase 2 Deployment Complete!"
echo "Completed: $(date +%H:%M)"
echo "=================================================="
echo
echo "📋 Next steps:"
echo "  1. Test API endpoints manually"
echo "  2. Deploy VP Bot service (optional)"
echo "  3. Review logs in CloudWatch"
echo "  4. Document final configuration"
echo
echo "See: /home/fredw/.openclaw/workspace/meeting-assistant-aws/pipeline/AWS-DEV-DEPLOYMENT-COMPLETE.md"
