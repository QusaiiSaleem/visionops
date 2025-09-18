-- VisionOps Supabase Database Schema
-- Run this in your Supabase SQL Editor to initialize the database

-- Enable required extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";
CREATE EXTENSION IF NOT EXISTS "pg_stat_statements";
CREATE EXTENSION IF NOT EXISTS "vector"; -- For Florence-2 embeddings

-- Create schema
CREATE SCHEMA IF NOT EXISTS visionops;

-- Set default schema
SET search_path TO visionops, public;

-- ============================================
-- TABLES
-- ============================================

-- Locations/Sites (for multi-location support)
CREATE TABLE IF NOT EXISTS locations (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name VARCHAR(255) NOT NULL,
    address TEXT,
    timezone VARCHAR(50) DEFAULT 'UTC',
    metadata JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- Cameras
CREATE TABLE IF NOT EXISTS cameras (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    location_id UUID REFERENCES locations(id) ON DELETE CASCADE,
    name VARCHAR(255) NOT NULL,
    rtsp_url TEXT NOT NULL,
    status VARCHAR(50) DEFAULT 'inactive',
    configuration JSONB,
    detection_zones JSONB,
    last_seen TIMESTAMPTZ,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(location_id, name)
);

-- Detections (aggregated from edge)
CREATE TABLE IF NOT EXISTS detections (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    camera_id UUID REFERENCES cameras(id) ON DELETE CASCADE,
    timestamp TIMESTAMPTZ NOT NULL,
    object_type VARCHAR(100) NOT NULL,
    confidence FLOAT NOT NULL CHECK (confidence >= 0 AND confidence <= 1),
    bounding_box JSONB,
    zone VARCHAR(100),
    metadata JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW(),

    -- Partitioning by day for better performance
    PRIMARY KEY (id, timestamp)
) PARTITION BY RANGE (timestamp);

-- Create partitions for the next 30 days
DO $$
DECLARE
    start_date DATE := CURRENT_DATE;
    end_date DATE := CURRENT_DATE + INTERVAL '30 days';
    partition_date DATE;
    partition_name TEXT;
BEGIN
    partition_date := start_date;
    WHILE partition_date < end_date LOOP
        partition_name := 'detections_' || to_char(partition_date, 'YYYY_MM_DD');

        EXECUTE format('
            CREATE TABLE IF NOT EXISTS %I PARTITION OF detections
            FOR VALUES FROM (%L) TO (%L)',
            partition_name,
            partition_date,
            partition_date + INTERVAL '1 day'
        );

        partition_date := partition_date + INTERVAL '1 day';
    END LOOP;
END $$;

-- Key frames with Florence-2 descriptions
CREATE TABLE IF NOT EXISTS key_frames (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    camera_id UUID REFERENCES cameras(id) ON DELETE CASCADE,
    timestamp TIMESTAMPTZ NOT NULL,
    image_data BYTEA, -- WebP compressed image (3-5KB)
    description TEXT, -- Florence-2 generated description
    embedding vector(768), -- Florence-2 embedding for similarity search
    objects_detected JSONB,
    metadata JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Aggregated metrics (100:1 compression from detections)
CREATE TABLE IF NOT EXISTS metrics (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    camera_id UUID REFERENCES cameras(id) ON DELETE CASCADE,
    timestamp TIMESTAMPTZ NOT NULL,
    window_minutes INTEGER DEFAULT 5,
    people_count INTEGER DEFAULT 0,
    vehicle_count INTEGER DEFAULT 0,
    avg_confidence FLOAT,
    max_occupancy INTEGER,
    zone_metrics JSONB,
    metadata JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW(),

    -- Unique constraint for time windows
    UNIQUE(camera_id, timestamp, window_minutes)
);

-- System health metrics
CREATE TABLE IF NOT EXISTS system_metrics (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    location_id UUID REFERENCES locations(id) ON DELETE CASCADE,
    timestamp TIMESTAMPTZ NOT NULL,
    cpu_usage FLOAT,
    memory_usage_mb INTEGER,
    disk_usage_gb FLOAT,
    temperature_celsius FLOAT,
    active_cameras INTEGER,
    frames_processed INTEGER,
    inference_latency_ms FLOAT,
    errors JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Alerts and notifications
CREATE TABLE IF NOT EXISTS alerts (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    location_id UUID REFERENCES locations(id) ON DELETE CASCADE,
    camera_id UUID REFERENCES cameras(id) ON DELETE CASCADE,
    type VARCHAR(100) NOT NULL,
    severity VARCHAR(20) CHECK (severity IN ('info', 'warning', 'error', 'critical')),
    message TEXT NOT NULL,
    details JSONB,
    acknowledged BOOLEAN DEFAULT FALSE,
    acknowledged_by UUID,
    acknowledged_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================
-- INDEXES
-- ============================================

-- Performance indexes
CREATE INDEX idx_cameras_location ON cameras(location_id);
CREATE INDEX idx_cameras_status ON cameras(status);

CREATE INDEX idx_detections_camera_time ON detections(camera_id, timestamp DESC);
CREATE INDEX idx_detections_type ON detections(object_type);
CREATE INDEX idx_detections_zone ON detections(zone);

CREATE INDEX idx_key_frames_camera_time ON key_frames(camera_id, timestamp DESC);
CREATE INDEX idx_key_frames_embedding ON key_frames USING ivfflat (embedding vector_cosine_ops);

CREATE INDEX idx_metrics_camera_time ON metrics(camera_id, timestamp DESC);
CREATE INDEX idx_metrics_window ON metrics(window_minutes);

CREATE INDEX idx_system_metrics_location_time ON system_metrics(location_id, timestamp DESC);

CREATE INDEX idx_alerts_location ON alerts(location_id);
CREATE INDEX idx_alerts_severity ON alerts(severity);
CREATE INDEX idx_alerts_acknowledged ON alerts(acknowledged);

-- ============================================
-- VIEWS
-- ============================================

-- Current camera status
CREATE OR REPLACE VIEW camera_status AS
SELECT
    c.id,
    c.name,
    l.name as location_name,
    c.status,
    c.last_seen,
    CASE
        WHEN c.last_seen > NOW() - INTERVAL '1 minute' THEN 'online'
        WHEN c.last_seen > NOW() - INTERVAL '5 minutes' THEN 'warning'
        ELSE 'offline'
    END as health_status,
    (SELECT COUNT(*) FROM detections d
     WHERE d.camera_id = c.id
     AND d.timestamp > NOW() - INTERVAL '1 hour') as detections_last_hour
FROM cameras c
JOIN locations l ON c.location_id = l.id;

-- Hourly statistics
CREATE OR REPLACE VIEW hourly_stats AS
SELECT
    camera_id,
    DATE_TRUNC('hour', timestamp) as hour,
    SUM(people_count) as total_people,
    SUM(vehicle_count) as total_vehicles,
    AVG(avg_confidence) as avg_confidence,
    MAX(max_occupancy) as peak_occupancy
FROM metrics
WHERE timestamp > NOW() - INTERVAL '24 hours'
GROUP BY camera_id, DATE_TRUNC('hour', timestamp);

-- ============================================
-- FUNCTIONS
-- ============================================

-- Function to clean old data (7-day retention)
CREATE OR REPLACE FUNCTION cleanup_old_data()
RETURNS void AS $$
BEGIN
    -- Delete old detections
    DELETE FROM detections WHERE timestamp < NOW() - INTERVAL '7 days';

    -- Delete old key frames
    DELETE FROM key_frames WHERE timestamp < NOW() - INTERVAL '7 days';

    -- Delete old metrics (keep 30 days)
    DELETE FROM metrics WHERE timestamp < NOW() - INTERVAL '30 days';

    -- Delete old system metrics
    DELETE FROM system_metrics WHERE timestamp < NOW() - INTERVAL '7 days';

    -- Delete old acknowledged alerts
    DELETE FROM alerts
    WHERE created_at < NOW() - INTERVAL '30 days'
    AND acknowledged = true;
END;
$$ LANGUAGE plpgsql;

-- Function to aggregate detections into metrics
CREATE OR REPLACE FUNCTION aggregate_detections(
    p_camera_id UUID,
    p_start_time TIMESTAMPTZ,
    p_end_time TIMESTAMPTZ
)
RETURNS void AS $$
DECLARE
    v_metrics RECORD;
BEGIN
    -- Calculate aggregated metrics
    SELECT
        COUNT(CASE WHEN object_type = 'person' THEN 1 END) as people_count,
        COUNT(CASE WHEN object_type IN ('car', 'truck', 'bus') THEN 1 END) as vehicle_count,
        AVG(confidence) as avg_confidence,
        COUNT(DISTINCT CASE WHEN object_type = 'person' THEN metadata->>'track_id' END) as max_occupancy,
        jsonb_object_agg(
            COALESCE(zone, 'unknown'),
            jsonb_build_object(
                'count', COUNT(*),
                'avg_confidence', AVG(confidence)
            )
        ) as zone_metrics
    INTO v_metrics
    FROM detections
    WHERE camera_id = p_camera_id
    AND timestamp >= p_start_time
    AND timestamp < p_end_time;

    -- Insert aggregated metrics
    INSERT INTO metrics (
        camera_id,
        timestamp,
        window_minutes,
        people_count,
        vehicle_count,
        avg_confidence,
        max_occupancy,
        zone_metrics
    ) VALUES (
        p_camera_id,
        p_start_time,
        EXTRACT(EPOCH FROM (p_end_time - p_start_time)) / 60,
        v_metrics.people_count,
        v_metrics.vehicle_count,
        v_metrics.avg_confidence,
        v_metrics.max_occupancy,
        v_metrics.zone_metrics
    )
    ON CONFLICT (camera_id, timestamp, window_minutes)
    DO UPDATE SET
        people_count = EXCLUDED.people_count,
        vehicle_count = EXCLUDED.vehicle_count,
        avg_confidence = EXCLUDED.avg_confidence,
        max_occupancy = EXCLUDED.max_occupancy,
        zone_metrics = EXCLUDED.zone_metrics,
        updated_at = NOW();
END;
$$ LANGUAGE plpgsql;

-- ============================================
-- ROW LEVEL SECURITY (RLS)
-- ============================================

-- Enable RLS
ALTER TABLE locations ENABLE ROW LEVEL SECURITY;
ALTER TABLE cameras ENABLE ROW LEVEL SECURITY;
ALTER TABLE detections ENABLE ROW LEVEL SECURITY;
ALTER TABLE key_frames ENABLE ROW LEVEL SECURITY;
ALTER TABLE metrics ENABLE ROW LEVEL SECURITY;
ALTER TABLE system_metrics ENABLE ROW LEVEL SECURITY;
ALTER TABLE alerts ENABLE ROW LEVEL SECURITY;

-- Policies (adjust based on your auth strategy)
-- For now, using service role for full access

-- Allow service role full access
CREATE POLICY "Service role full access" ON locations
    FOR ALL USING (auth.role() = 'service_role');

CREATE POLICY "Service role full access" ON cameras
    FOR ALL USING (auth.role() = 'service_role');

CREATE POLICY "Service role full access" ON detections
    FOR ALL USING (auth.role() = 'service_role');

CREATE POLICY "Service role full access" ON key_frames
    FOR ALL USING (auth.role() = 'service_role');

CREATE POLICY "Service role full access" ON metrics
    FOR ALL USING (auth.role() = 'service_role');

CREATE POLICY "Service role full access" ON system_metrics
    FOR ALL USING (auth.role() = 'service_role');

CREATE POLICY "Service role full access" ON alerts
    FOR ALL USING (auth.role() = 'service_role');

-- ============================================
-- SCHEDULED JOBS (using pg_cron if available)
-- ============================================

-- Note: Enable pg_cron extension in Supabase dashboard first
-- Then uncomment these:

-- SELECT cron.schedule(
--     'cleanup-old-data',
--     '0 3 * * *', -- Daily at 3 AM
--     'SELECT cleanup_old_data();'
-- );

-- SELECT cron.schedule(
--     'aggregate-metrics',
--     '*/5 * * * *', -- Every 5 minutes
--     'SELECT aggregate_detections(
--         camera_id,
--         NOW() - INTERVAL ''5 minutes'',
--         NOW()
--     ) FROM cameras WHERE status = ''active'';'
-- );

-- ============================================
-- INITIAL DATA
-- ============================================

-- Insert default location
INSERT INTO locations (name, address, timezone, metadata)
VALUES (
    'Main Office',
    'Your Address Here',
    'America/New_York',
    '{"type": "office", "contact": "admin@visionops.com"}'
) ON CONFLICT DO NOTHING;

-- ============================================
-- PERMISSIONS GRANT
-- ============================================

-- Grant usage on schema
GRANT USAGE ON SCHEMA visionops TO anon, authenticated, service_role;

-- Grant permissions on tables
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA visionops TO service_role;
GRANT SELECT ON ALL TABLES IN SCHEMA visionops TO anon, authenticated;

-- Grant permissions on sequences
GRANT USAGE ON ALL SEQUENCES IN SCHEMA visionops TO service_role;

-- ============================================
-- SUCCESS MESSAGE
-- ============================================

DO $$
BEGIN
    RAISE NOTICE 'VisionOps database schema created successfully!';
    RAISE NOTICE 'Next steps:';
    RAISE NOTICE '1. Enable pg_cron extension for scheduled jobs';
    RAISE NOTICE '2. Copy your Supabase URL and anon key';
    RAISE NOTICE '3. Configure them in VisionOps UI settings';
END $$;