# Setting Up Supabase for VisionOps

## 🚀 Quick Setup Guide

### Step 1: Create Supabase Project

1. **Go to [Supabase Dashboard](https://app.supabase.com)**
2. **Click "New Project"**
3. **Enter details:**
   - Project name: `visionops` (or your preferred name)
   - Database Password: (save this securely)
   - Region: Choose closest to your location
   - Pricing Plan: Free tier is fine for testing

4. **Wait for project to initialize** (takes 2-3 minutes)

### Step 2: Initialize Database Schema

1. **Open SQL Editor** in your Supabase project
2. **Copy the entire contents** of `supabase/init-supabase.sql`
3. **Paste and run** in the SQL editor
4. **You should see:** "VisionOps database schema created successfully!"

### Step 3: Enable Required Extensions

1. Go to **Database → Extensions**
2. Enable these if not already enabled:
   - `uuid-ossp` (UUID generation)
   - `pgcrypto` (Encryption)
   - `vector` (For AI embeddings)
   - `pg_cron` (Optional - for scheduled jobs)

### Step 4: Get Your Connection Details

1. Go to **Settings → API**
2. Copy these values:
   ```
   Project URL: https://YOUR_PROJECT.supabase.co
   Anon Key: eyJ....... (long string)
   Service Role Key: eyJ....... (keep this secret!)
   ```

### Step 5: Configure VisionOps

#### Option A: Using the UI
1. Open VisionOps Configuration UI
2. Go to Settings tab
3. Enter:
   - Supabase URL: `https://YOUR_PROJECT.supabase.co`
   - Supabase Key: `your-anon-key`
4. Click "Test Connection"
5. Save Settings

#### Option B: Edit Configuration File
1. Open `C:\Program Files\VisionOps\Service\appsettings.json`
2. Update:
```json
{
  "VisionOps": {
    "Supabase": {
      "Url": "https://YOUR_PROJECT.supabase.co",
      "AnonKey": "your-anon-key-here",
      "ServiceRoleKey": "your-service-role-key-here"
    }
  }
}
```
3. Restart VisionOps service

### Step 6: Verify Connection

1. **Check VisionOps logs** for successful connection
2. **Check Supabase Dashboard**:
   - Go to Table Editor
   - You should see data appearing in:
     - `locations` table
     - `cameras` table (when you add cameras)
     - `system_metrics` table (health data)

## 📊 Supabase Dashboard Setup

### Create a Dashboard (Optional)

1. **Go to SQL Editor**
2. **Create useful queries:**

```sql
-- Active cameras
SELECT * FROM visionops.camera_status
WHERE health_status = 'online';

-- Today's detections
SELECT
  camera_id,
  COUNT(*) as detection_count,
  COUNT(DISTINCT object_type) as unique_objects
FROM visionops.detections
WHERE timestamp > CURRENT_DATE
GROUP BY camera_id;

-- System health
SELECT * FROM visionops.system_metrics
ORDER BY timestamp DESC
LIMIT 100;
```

3. **Save queries** for quick access

## 🔐 Security Best Practices

1. **Never expose Service Role Key** in client apps
2. **Use Row Level Security (RLS)** - already configured
3. **Rotate keys periodically**
4. **Monitor usage** in Supabase dashboard

## 🚦 Testing the Integration

### From VisionOps UI:
1. Add a camera
2. Start the service
3. Wait 30 seconds (sync interval)
4. Check Supabase Table Editor
5. You should see:
   - Camera in `cameras` table
   - System metrics appearing
   - Detections (once cameras are processing)

### From Supabase:
```sql
-- Check if data is syncing
SELECT COUNT(*) FROM visionops.system_metrics
WHERE created_at > NOW() - INTERVAL '5 minutes';

-- View latest camera status
SELECT * FROM visionops.camera_status;
```

## 🛠️ Troubleshooting

### Connection Failed
- Check firewall isn't blocking HTTPS (port 443)
- Verify URL doesn't have trailing slash
- Ensure keys are copied correctly (no spaces)

### No Data Appearing
- Check VisionOps service is running
- Review logs at `C:\ProgramData\VisionOps\Logs`
- Verify sync interval (default 30 seconds)

### Authentication Errors
- Make sure you're using the **anon** key, not the service key
- Check key hasn't expired or been regenerated

## 📈 Using the Data

Once data is flowing to Supabase, you can:

1. **Build a web dashboard** using:
   - Next.js + Supabase JS client
   - Retool/Appsmith for quick dashboards
   - Power BI with PostgreSQL connector

2. **Set up alerts** using:
   - Supabase Edge Functions
   - Webhooks to Slack/Teams
   - Email notifications

3. **Analytics** with:
   - SQL queries for reports
   - Export to data warehouse
   - ML pipelines with the embeddings

## 🔄 Automatic Schema Updates

When VisionOps updates include schema changes:
1. Check release notes for migration scripts
2. Run migrations in SQL editor
3. No data loss - migrations are always safe

## 📝 Environment Variables (Alternative Setup)

Instead of hardcoding in config, you can use environment variables:

```powershell
# Set as system environment variables
[System.Environment]::SetEnvironmentVariable("VISIONOPS_SUPABASE_URL", "https://YOUR_PROJECT.supabase.co", "Machine")
[System.Environment]::SetEnvironmentVariable("VISIONOPS_SUPABASE_KEY", "your-anon-key", "Machine")

# Restart VisionOps service to pick up changes
Restart-Service VisionOps
```

## ✅ Success Checklist

- [ ] Supabase project created
- [ ] Database schema initialized
- [ ] Extensions enabled
- [ ] Connection details obtained
- [ ] VisionOps configured
- [ ] Connection tested successfully
- [ ] Data syncing verified

---

**Need Help?**
- Check logs: `C:\ProgramData\VisionOps\Logs\`
- Supabase Discord: https://discord.supabase.com
- VisionOps Issues: https://github.com/visionops/visionops/issues