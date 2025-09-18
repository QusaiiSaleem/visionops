import { useState, useEffect } from "react";
import { Activity, Camera, AlertTriangle, CheckCircle, Clock, Zap } from "lucide-react";
import { Card } from "./ui/card";
import { Label } from "./ui/label";
import { Badge } from "./ui/badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./ui/table";
import { Progress } from "./ui/progress";

interface FrameLog {
  id: string;
  cameraId: string;
  cameraName: string;
  timestamp: Date;
  frameSize: number;
  processTime: number;
  status: "success" | "error" | "processing";
  detections: number;
}

interface CameraHealth {
  cameraId: string;
  cameraName: string;
  status: "healthy" | "warning" | "critical";
  fps: number;
  targetFps: number;
  framesLastHour: number;
  averageProcessTime: number;
  errorRate: number;
  lastSeen: Date;
}

export function FrameMonitoring() {
  const [frameLogs, setFrameLogs] = useState<FrameLog[]>([
    {
      id: "1",
      cameraId: "cam1",
      cameraName: "Front Entrance",
      timestamp: new Date(Date.now() - 2000),
      frameSize: 245760,
      processTime: 150,
      status: "success",
      detections: 2
    },
    {
      id: "2", 
      cameraId: "cam2",
      cameraName: "Loading Dock",
      timestamp: new Date(Date.now() - 5000),
      frameSize: 307200,
      processTime: 89,
      status: "success",
      detections: 1
    },
    {
      id: "3",
      cameraId: "cam1", 
      cameraName: "Front Entrance",
      timestamp: new Date(Date.now() - 8000),
      frameSize: 245760,
      processTime: 201,
      status: "processing",
      detections: 0
    }
  ]);

  const [cameraHealth, setCameraHealth] = useState<CameraHealth[]>([
    {
      cameraId: "cam1",
      cameraName: "Front Entrance",
      status: "healthy",
      fps: 24,
      targetFps: 25,
      framesLastHour: 1440,
      averageProcessTime: 145,
      errorRate: 0.2,
      lastSeen: new Date(Date.now() - 2000)
    },
    {
      cameraId: "cam2",
      cameraName: "Loading Dock", 
      status: "healthy",
      fps: 29,
      targetFps: 30,
      framesLastHour: 1750,
      averageProcessTime: 92,
      errorRate: 0.1,
      lastSeen: new Date(Date.now() - 5000)
    },
    {
      cameraId: "cam3",
      cameraName: "Office Area",
      status: "critical",
      fps: 0,
      targetFps: 15,
      framesLastHour: 0,
      averageProcessTime: 0,
      errorRate: 100,
      lastSeen: new Date(Date.now() - 300000)
    }
  ]);

  // Simulate real-time updates
  useEffect(() => {
    const interval = setInterval(() => {
      // Add new frame log
      const newLog: FrameLog = {
        id: Date.now().toString(),
        cameraId: Math.random() > 0.5 ? "cam1" : "cam2",
        cameraName: Math.random() > 0.5 ? "Front Entrance" : "Loading Dock",
        timestamp: new Date(),
        frameSize: Math.floor(Math.random() * 100000) + 200000,
        processTime: Math.floor(Math.random() * 100) + 50,
        status: Math.random() > 0.1 ? "success" : "error",
        detections: Math.floor(Math.random() * 5)
      };

      setFrameLogs(prev => [newLog, ...prev.slice(0, 19)]);

      // Update camera health
      setCameraHealth(prev => prev.map(cam => ({
        ...cam,
        framesLastHour: cam.status !== "critical" ? cam.framesLastHour + 1 : cam.framesLastHour,
        lastSeen: cam.status !== "critical" ? new Date() : cam.lastSeen
      })));
    }, 2000);

    return () => clearInterval(interval);
  }, []);

  const getHealthIcon = (status: CameraHealth["status"]) => {
    switch (status) {
      case "healthy":
        return <CheckCircle className="h-4 w-4 text-green-500" />;
      case "warning":
        return <AlertTriangle className="h-4 w-4 text-yellow-500" />;
      case "critical":
        return <AlertTriangle className="h-4 w-4 text-red-500" />;
    }
  };

  const getHealthBadge = (status: CameraHealth["status"]) => {
    switch (status) {
      case "healthy":
        return <Badge variant="default">Healthy</Badge>;
      case "warning":
        return <Badge variant="secondary">Warning</Badge>;
      case "critical":
        return <Badge variant="destructive">Critical</Badge>;
    }
  };

  const getStatusIcon = (status: FrameLog["status"]) => {
    switch (status) {
      case "success":
        return <CheckCircle className="h-4 w-4 text-green-500" />;
      case "error":
        return <AlertTriangle className="h-4 w-4 text-red-500" />;
      case "processing":
        return <Clock className="h-4 w-4 text-blue-500" />;
    }
  };

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return bytes + " B";
    if (bytes < 1024 * 1024) return Math.round(bytes / 1024) + " KB";
    return Math.round(bytes / (1024 * 1024)) + " MB";
  };

  const formatTimestamp = (date: Date) => {
    return date.toLocaleTimeString();
  };

  return (
    <div className="space-y-6">
      <div>
        <h1>Frame Monitoring</h1>
        <p className="text-muted-foreground mt-1">
          Real-time camera health and frame processing logs
        </p>
      </div>

      {/* Camera Health Overview */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {cameraHealth.map((camera) => (
          <Card key={camera.cameraId} className="p-4">
            <div className="space-y-3">
              <div className="flex items-center justify-between">
                <div className="flex items-center space-x-2">
                  <Camera className="h-4 w-4" />
                  <span className="font-medium">{camera.cameraName}</span>
                </div>
                {getHealthBadge(camera.status)}
              </div>

              <div className="grid grid-cols-2 gap-4 text-sm">
                <div>
                  <div className="text-muted-foreground">FPS</div>
                  <div className="font-medium">
                    {camera.fps}/{camera.targetFps}
                  </div>
                  <Progress 
                    value={(camera.fps / camera.targetFps) * 100} 
                    className="mt-1 h-2"
                  />
                </div>
                <div>
                  <div className="text-muted-foreground">Process Time</div>
                  <div className="font-medium">{camera.averageProcessTime}ms</div>
                </div>
                <div>
                  <div className="text-muted-foreground">Frames/Hour</div>
                  <div className="font-medium">{camera.framesLastHour.toLocaleString()}</div>
                </div>
                <div>
                  <div className="text-muted-foreground">Error Rate</div>
                  <div className="font-medium">{camera.errorRate}%</div>
                </div>
              </div>

              <div className="pt-2 border-t">
                <div className="text-xs text-muted-foreground">
                  Last seen: {formatTimestamp(camera.lastSeen)}
                </div>
              </div>
            </div>
          </Card>
        ))}
      </div>

      {/* Real-time Frame Logs */}
      <Card>
        <div className="p-6">
          <div className="flex items-center justify-between mb-4">
            <Label>Live Frame Processing Logs</Label>
            <div className="flex items-center gap-2">
              <Activity className="h-4 w-4 text-green-500" />
              <span className="text-sm text-muted-foreground">Real-time</span>
            </div>
          </div>

          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Time</TableHead>
                <TableHead>Camera</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Frame Size</TableHead>
                <TableHead>Process Time</TableHead>
                <TableHead>Detections</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {frameLogs.map((log) => (
                <TableRow key={log.id} className={log.status === "error" ? "bg-red-50 dark:bg-red-950/20" : ""}>
                  <TableCell className="font-mono text-sm">
                    {formatTimestamp(log.timestamp)}
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      <Camera className="h-4 w-4" />
                      {log.cameraName}
                    </div>
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      {getStatusIcon(log.status)}
                      <span className="capitalize">{log.status}</span>
                    </div>
                  </TableCell>
                  <TableCell>{formatFileSize(log.frameSize)}</TableCell>
                  <TableCell>
                    <div className="flex items-center gap-1">
                      <Zap className="h-3 w-3" />
                      {log.processTime}ms
                    </div>
                  </TableCell>
                  <TableCell>
                    {log.detections > 0 ? (
                      <Badge variant="outline">{log.detections} detected</Badge>
                    ) : (
                      <span className="text-muted-foreground">None</span>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>

          {frameLogs.length === 0 && (
            <div className="text-center py-8">
              <Activity className="h-12 w-12 text-muted-foreground mx-auto mb-4 opacity-50" />
              <p className="text-muted-foreground">No frame processing activity</p>
              <p className="text-sm text-muted-foreground">Waiting for camera data...</p>
            </div>
          )}
        </div>
      </Card>
    </div>
  );
}