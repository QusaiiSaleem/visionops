import { useState, useRef, useCallback } from "react";
import { Square, Circle, Pentagon, Trash2, Save, RotateCcw } from "lucide-react";
import { Button } from "./ui/button";
import { Card } from "./ui/card";
import { Label } from "./ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "./ui/select";
import { Badge } from "./ui/badge";

interface Zone {
  id: string;
  name: string;
  type: "rectangle" | "circle" | "polygon";
  coordinates: number[];
  color: string;
}

export function DetectionZones() {
  const [selectedCamera, setSelectedCamera] = useState("front-entrance");
  const [selectedTool, setSelectedTool] = useState<"rectangle" | "circle" | "polygon" | null>(null);
  const [zones, setZones] = useState<Zone[]>([
    { id: "1", name: "Entrance Area", type: "rectangle", coordinates: [100, 100, 300, 200], color: "#3b82f6" },
    { id: "2", name: "Counter Zone", type: "circle", coordinates: [400, 150, 80], color: "#10b981" },
  ]);
  const [isDrawing, setIsDrawing] = useState(false);
  const [currentZone, setCurrentZone] = useState<Partial<Zone> | null>(null);
  const canvasRef = useRef<HTMLDivElement>(null);

  const cameras = [
    { value: "front-entrance", label: "Front Entrance" },
    { value: "loading-dock", label: "Loading Dock" },
    { value: "office-area", label: "Office Area" },
  ];

  const handleToolSelect = (tool: "rectangle" | "circle" | "polygon") => {
    setSelectedTool(selectedTool === tool ? null : tool);
  };

  const handleCanvasClick = useCallback((event: React.MouseEvent) => {
    if (!selectedTool || !canvasRef.current) return;

    const rect = canvasRef.current.getBoundingClientRect();
    const x = event.clientX - rect.left;
    const y = event.clientY - rect.top;

    if (!isDrawing) {
      // Start drawing
      setIsDrawing(true);
      setCurrentZone({
        id: Date.now().toString(),
        name: `Zone ${zones.length + 1}`,
        type: selectedTool,
        coordinates: selectedTool === "circle" ? [x, y, 50] : [x, y, x, y],
        color: ["#3b82f6", "#10b981", "#f59e0b", "#ef4444", "#8b5cf6"][zones.length % 5],
      });
    } else {
      // Finish drawing
      if (currentZone) {
        const newZone = { ...currentZone } as Zone;
        setZones([...zones, newZone]);
      }
      setIsDrawing(false);
      setCurrentZone(null);
      setSelectedTool(null);
    }
  }, [selectedTool, isDrawing, zones, currentZone]);

  const handleMouseMove = useCallback((event: React.MouseEvent) => {
    if (!isDrawing || !currentZone || !canvasRef.current) return;

    const rect = canvasRef.current.getBoundingClientRect();
    const x = event.clientX - rect.left;
    const y = event.clientY - rect.top;

    if (currentZone.type === "rectangle") {
      const [startX, startY] = currentZone.coordinates!;
      setCurrentZone({
        ...currentZone,
        coordinates: [startX, startY, x, y],
      });
    } else if (currentZone.type === "circle") {
      const [centerX, centerY] = currentZone.coordinates!;
      const radius = Math.sqrt((x - centerX) ** 2 + (y - centerY) ** 2);
      setCurrentZone({
        ...currentZone,
        coordinates: [centerX, centerY, radius],
      });
    }
  }, [isDrawing, currentZone]);

  const handleDeleteZone = (zoneId: string) => {
    setZones(zones.filter(zone => zone.id !== zoneId));
  };

  const handleClearAll = () => {
    setZones([]);
    setCurrentZone(null);
    setIsDrawing(false);
    setSelectedTool(null);
  };

  const renderZone = (zone: Zone) => {
    if (zone.type === "rectangle") {
      const [x1, y1, x2, y2] = zone.coordinates;
      const width = Math.abs(x2 - x1);
      const height = Math.abs(y2 - y1);
      const x = Math.min(x1, x2);
      const y = Math.min(y1, y2);
      
      return (
        <div
          key={zone.id}
          className="absolute border-2 bg-opacity-20"
          style={{
            left: x,
            top: y,
            width,
            height,
            borderColor: zone.color,
            backgroundColor: zone.color + "33",
          }}
        />
      );
    } else if (zone.type === "circle") {
      const [centerX, centerY, radius] = zone.coordinates;
      
      return (
        <div
          key={zone.id}
          className="absolute border-2 rounded-full bg-opacity-20"
          style={{
            left: centerX - radius,
            top: centerY - radius,
            width: radius * 2,
            height: radius * 2,
            borderColor: zone.color,
            backgroundColor: zone.color + "33",
          }}
        />
      );
    }
    return null;
  };

  return (
    <div className="space-y-6">
      <div>
        <h1>Detection Zones</h1>
        <p className="text-muted-foreground mt-1">
          Draw detection zones for AI analysis
        </p>
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-4 gap-6">
        <div className="xl:col-span-3">
          <Card className="p-6">
            <div className="flex items-center justify-between mb-4">
              <div className="flex items-center space-x-4">
                <div>
                  <Label>Camera</Label>
                  <Select value={selectedCamera} onValueChange={setSelectedCamera}>
                    <SelectTrigger className="w-48 mt-1">
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

                <div className="flex items-center space-x-2">
                  <Button
                    variant={selectedTool === "rectangle" ? "default" : "outline"}
                    size="sm"
                    onClick={() => handleToolSelect("rectangle")}
                  >
                    <Square className="h-4 w-4" />
                  </Button>
                  <Button
                    variant={selectedTool === "circle" ? "default" : "outline"}
                    size="sm"
                    onClick={() => handleToolSelect("circle")}
                  >
                    <Circle className="h-4 w-4" />
                  </Button>
                  <Button
                    variant={selectedTool === "polygon" ? "default" : "outline"}
                    size="sm"
                    onClick={() => handleToolSelect("polygon")}
                    disabled
                  >
                    <Pentagon className="h-4 w-4" />
                  </Button>
                </div>
              </div>

              <div className="flex items-center space-x-2">
                <Button variant="outline" size="sm" onClick={handleClearAll}>
                  <RotateCcw className="h-4 w-4" />
                </Button>
                <Button size="sm">
                  <Save className="h-4 w-4 mr-2" />
                  Save Zones
                </Button>
              </div>
            </div>

            <div 
              ref={canvasRef}
              className="relative aspect-video bg-muted rounded-lg overflow-hidden cursor-crosshair"
              onClick={handleCanvasClick}
              onMouseMove={handleMouseMove}
            >
              {/* Simulated camera feed */}
              <div className="absolute inset-0 bg-gradient-to-br from-gray-300 to-gray-400 flex items-center justify-center">
                <div className="text-center text-white">
                  <div className="text-xl font-semibold">Camera Feed</div>
                  <div className="text-sm opacity-75">Click and drag to draw zones</div>
                </div>
              </div>

              {/* Render existing zones */}
              {zones.map(renderZone)}

              {/* Render current zone being drawn */}
              {currentZone && renderZone(currentZone as Zone)}

              {selectedTool && (
                <div className="absolute top-2 left-2">
                  <Badge variant="secondary">
                    Drawing {selectedTool} - Click to start
                  </Badge>
                </div>
              )}
            </div>
          </Card>
        </div>

        <div className="space-y-4">
          <Card className="p-4">
            <div className="flex items-center justify-between mb-3">
              <Label>Active Zones ({zones.length})</Label>
            </div>
            
            <div className="space-y-2">
              {zones.length === 0 ? (
                <div className="text-center py-4 text-muted-foreground text-sm">
                  No zones defined
                </div>
              ) : (
                zones.map((zone) => (
                  <div key={zone.id} className="flex items-center justify-between p-2 bg-muted rounded">
                    <div className="flex items-center space-x-2">
                      <div 
                        className="w-3 h-3 rounded-sm border"
                        style={{ backgroundColor: zone.color }}
                      />
                      <div>
                        <div className="text-sm font-medium">{zone.name}</div>
                        <div className="text-xs text-muted-foreground capitalize">
                          {zone.type}
                        </div>
                      </div>
                    </div>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleDeleteZone(zone.id)}
                    >
                      <Trash2 className="h-3 w-3" />
                    </Button>
                  </div>
                ))
              )}
            </div>
          </Card>

          <Card className="p-4">
            <Label className="mb-3 block">Instructions</Label>
            <div className="space-y-2 text-sm text-muted-foreground">
              <p>1. Select a drawing tool</p>
              <p>2. Click to start drawing</p>
              <p>3. Move mouse to resize</p>
              <p>4. Click again to finish</p>
              <p>5. Save your zones</p>
            </div>
          </Card>
        </div>
      </div>
    </div>
  );
}