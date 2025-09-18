import { useState, useRef, useEffect } from "react";
import { Play, Square, Camera, Database, Eye, Users, Car, Package } from "lucide-react";
import { Button } from "./ui/button";
import { Card } from "./ui/card";
import { Label } from "./ui/label";
import { Badge } from "./ui/badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./ui/table";
import { Progress } from "./ui/progress";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "./ui/select";

interface AnalysisData {
  id: string;
  timestamp: Date;
  cameraId: string;
  cameraName: string;
  detectionType: "person" | "vehicle" | "object" | "motion";
  confidence: number;
  coordinates: { x: number; y: number; width: number; height: number };
  metadata: {
    count: number;
    attributes: string[];
  };
  synced: boolean;
}

interface DetectionStats {
  type: string;
  count: number;
  confidence: number;
  icon: React.ReactNode;
}

export function DataAnalysis() {
  const [isPreviewActive, setIsPreviewActive] = useState(false);
  const [selectedCamera, setSelectedCamera] = useState("demo");
  const [analysisData, setAnalysisData] = useState<AnalysisData[]>([
    {
      id: "1",
      timestamp: new Date(Date.now() - 30000),
      cameraId: "cam1",
      cameraName: "Front Entrance",
      detectionType: "person",
      confidence: 0.92,
      coordinates: { x: 100, y: 50, width: 80, height: 200 },
      metadata: { count: 2, attributes: ["walking", "adult"] },
      synced: true
    },
    {
      id: "2",
      timestamp: new Date(Date.now() - 45000),
      cameraId: "cam2", 
      cameraName: "Loading Dock",
      detectionType: "vehicle",
      confidence: 0.87,
      coordinates: { x: 200, y: 100, width: 150, height: 100 },
      metadata: { count: 1, attributes: ["truck", "commercial"] },
      synced: true
    },
    {
      id: "3",
      timestamp: new Date(Date.now() - 60000),
      cameraId: "cam1",
      cameraName: "Front Entrance", 
      detectionType: "object",
      confidence: 0.76,
      coordinates: { x: 50, y: 150, width: 60, height: 80 },
      metadata: { count: 1, attributes: ["package", "delivery"] },
      synced: false
    }
  ]);

  const videoRef = useRef<HTMLVideoElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [currentDetections, setCurrentDetections] = useState<DetectionStats[]>([]);

  const cameras = [
    { value: "demo", label: "PC Camera (Demo)" },
    { value: "cam1", label: "Front Entrance" },
    { value: "cam2", label: "Loading Dock" },
    { value: "cam3", label: "Office Area" }
  ];

  const startPreview = async () => {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ 
        video: { width: 640, height: 480 } 
      });
      if (videoRef.current) {
        videoRef.current.srcObject = stream;
        setIsPreviewActive(true);
        
        // Simulate AI detections
        simulateDetections();
      }
    } catch (err) {
      console.error("Error accessing camera:", err);
    }
  };

  const stopPreview = () => {
    if (videoRef.current && videoRef.current.srcObject) {
      const tracks = (videoRef.current.srcObject as MediaStream).getTracks();
      tracks.forEach(track => track.stop());
      videoRef.current.srcObject = null;
    }
    setIsPreviewActive(false);
    setCurrentDetections([]);
  };

  const simulateDetections = () => {
    const detectionTypes = [
      { type: "Person", icon: <Users className="h-4 w-4" />, baseCount: 0 },
      { type: "Vehicle", icon: <Car className="h-4 w-4" />, baseCount: 0 },
      { type: "Object", icon: <Package className="h-4 w-4" />, baseCount: 0 }
    ];

    const interval = setInterval(() => {
      const newDetections = detectionTypes.map(det => ({
        type: det.type,
        count: Math.floor(Math.random() * 3),
        confidence: Math.random() * 0.4 + 0.6,
        icon: det.icon
      }));
      setCurrentDetections(newDetections);

      // Add to analysis data
      if (newDetections.some(d => d.count > 0)) {
        const detection = newDetections.find(d => d.count > 0);
        if (detection) {
          const newAnalysis: AnalysisData = {
            id: Date.now().toString(),
            timestamp: new Date(),
            cameraId: "demo",
            cameraName: "PC Camera (Demo)",
            detectionType: detection.type.toLowerCase() as any,
            confidence: detection.confidence,
            coordinates: { x: Math.random() * 400, y: Math.random() * 300, width: 80, height: 120 },
            metadata: { count: detection.count, attributes: ["live-demo"] },
            synced: false
          };
          setAnalysisData(prev => [newAnalysis, ...prev.slice(0, 19)]);
        }
      }
    }, 3000);

    return interval;
  };

  useEffect(() => {
    let interval: NodeJS.Timeout;
    if (isPreviewActive) {
      interval = simulateDetections();
    }
    return () => {
      if (interval) clearInterval(interval);
    };
  }, [isPreviewActive]);

  const getDetectionIcon = (type: AnalysisData["detectionType"]) => {
    switch (type) {
      case "person":
        return <Users className="h-4 w-4" />;
      case "vehicle":
        return <Car className="h-4 w-4" />;
      case "object":
        return <Package className="h-4 w-4" />;
      default:
        return <Eye className="h-4 w-4" />;
    }
  };

  const formatTimestamp = (date: Date) => {
    return date.toLocaleTimeString();
  };

  return (
    <div className="space-y-6">
      <div>
        <h1>Data Analysis</h1>
        <p className="text-muted-foreground mt-1">
          Analyze detection data and preview AI processing with live demo
        </p>
      </div>

      {/* Live Demo Section */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Card className="p-6">
          <div className="space-y-4">
            <div className="flex items-center justify-between">
              <Label>Live AI Demo</Label>
              {isPreviewActive && <Badge variant="default">Processing</Badge>}
            </div>

            <div>
              <Label>Camera Source</Label>
              <Select value={selectedCamera} onValueChange={setSelectedCamera}>
                <SelectTrigger className="mt-1">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {cameras.map(camera => (
                    <SelectItem key={camera.value} value={camera.value}>
                      {camera.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="aspect-video bg-muted rounded-lg overflow-hidden relative">
              {selectedCamera === "demo" ? (
                <>
                  <video 
                    ref={videoRef}
                    autoPlay 
                    muted 
                    className="w-full h-full object-cover"
                    style={{ display: isPreviewActive ? 'block' : 'none' }}
                  />
                  <canvas 
                    ref={canvasRef}
                    className="absolute top-0 left-0 w-full h-full"
                    style={{ display: isPreviewActive ? 'block' : 'none' }}
                  />
                  {!isPreviewActive && (
                    <div className="absolute inset-0 flex items-center justify-center">
                      <div className="text-center">
                        <Camera className="h-12 w-12 text-muted-foreground mx-auto mb-2" />
                        <p className="text-muted-foreground">Click start to begin live demo</p>
                      </div>
                    </div>
                  )}
                </>
              ) : (
                <div className="absolute inset-0 bg-gradient-to-br from-gray-300 to-gray-400 flex items-center justify-center">
                  <div className="text-center text-white">
                    <Camera className="h-12 w-12 mx-auto mb-2" />
                    <p>Camera Feed Simulation</p>
                    <p className="text-sm opacity-75">{cameras.find(c => c.value === selectedCamera)?.label}</p>
                  </div>
                </div>
              )}
            </div>

            <div className="flex gap-2">
              {!isPreviewActive ? (
                <Button onClick={startPreview} className="flex-1">
                  <Play className="h-4 w-4 mr-2" />
                  Start Demo
                </Button>
              ) : (
                <Button onClick={stopPreview} variant="destructive" className="flex-1">
                  <Square className="h-4 w-4 mr-2" />
                  Stop Demo
                </Button>
              )}
            </div>
          </div>
        </Card>

        <Card className="p-6">
          <div className="space-y-4">
            <Label>Real-time Detections</Label>
            
            {currentDetections.length > 0 ? (
              <div className="space-y-3">
                {currentDetections.map((detection, index) => (
                  <div key={index} className="flex items-center justify-between p-3 bg-muted rounded">
                    <div className="flex items-center gap-2">
                      {detection.icon}
                      <span className="font-medium">{detection.type}</span>
                    </div>
                    <div className="text-right">
                      <div className="font-semibold">{detection.count}</div>
                      <div className="text-xs text-muted-foreground">
                        {Math.round(detection.confidence * 100)}% confidence
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <div className="text-center py-8 text-muted-foreground">
                <Eye className="h-8 w-8 mx-auto mb-2 opacity-50" />
                <p>No active detections</p>
                <p className="text-sm">Start demo to see AI analysis</p>
              </div>
            )}

            {isPreviewActive && (
              <div className="pt-4 border-t space-y-2">
                <div className="flex justify-between text-sm">
                  <span>Processing Rate:</span>
                  <span>15 FPS</span>
                </div>
                <div className="flex justify-between text-sm">
                  <span>Analysis Time:</span>
                  <span>~67ms/frame</span>
                </div>
                <Progress value={87} className="mt-2" />
                <p className="text-xs text-muted-foreground">GPU utilization: 87%</p>
              </div>
            )}
          </div>
        </Card>
      </div>

      {/* Analysis Data Table */}
      <Card>
        <div className="p-6">
          <div className="flex items-center justify-between mb-4">
            <Label>Frame Analysis Data</Label>
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <Database className="h-4 w-4" />
              <span>{analysisData.filter(d => d.synced).length} synced to database</span>
            </div>
          </div>

          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Time</TableHead>
                <TableHead>Camera</TableHead>
                <TableHead>Detection</TableHead>
                <TableHead>Confidence</TableHead>
                <TableHead>Count</TableHead>
                <TableHead>Attributes</TableHead>
                <TableHead>Sync Status</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {analysisData.map((data) => (
                <TableRow key={data.id}>
                  <TableCell className="font-mono text-sm">
                    {formatTimestamp(data.timestamp)}
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      <Camera className="h-4 w-4" />
                      {data.cameraName}
                    </div>
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      {getDetectionIcon(data.detectionType)}
                      <span className="capitalize">{data.detectionType}</span>
                    </div>
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      <Progress value={data.confidence * 100} className="w-16 h-2" />
                      <span className="text-sm">{Math.round(data.confidence * 100)}%</span>
                    </div>
                  </TableCell>
                  <TableCell>{data.metadata.count}</TableCell>
                  <TableCell>
                    <div className="flex gap-1">
                      {data.metadata.attributes.map((attr, i) => (
                        <Badge key={i} variant="outline" className="text-xs">
                          {attr}
                        </Badge>
                      ))}
                    </div>
                  </TableCell>
                  <TableCell>
                    {data.synced ? (
                      <Badge variant="default">Synced</Badge>
                    ) : (
                      <Badge variant="secondary">Pending</Badge>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>

          {analysisData.length === 0 && (
            <div className="text-center py-8">
              <Database className="h-12 w-12 text-muted-foreground mx-auto mb-4 opacity-50" />
              <p className="text-muted-foreground">No analysis data available</p>
              <p className="text-sm text-muted-foreground">Start capturing frames to see analysis results</p>
            </div>
          )}
        </div>
      </Card>
    </div>
  );
}