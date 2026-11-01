import { useEffect, useRef, useState } from 'react';
import { applyRegionEffect } from './captureImageEffects';

export type CaptureEditorState = { image: string; initialTool: number; workflowCompletion: boolean; status: string };
type Point = { x: number; y: number };
const Tool = { Freehand: 0, Rectangle: 1, Ellipse: 2, Line: 3, Arrow: 4, Text: 5, Step: 6, Highlight: 7, Blur: 8, Pixelate: 9, Redact: 10, Crop: 11 } as const;
const MAX_UNDO_STEPS = 20;
const MAX_HISTORY_BYTES = 128 * 1024 * 1024;

const captureTools = ['Serbest çizim', 'Dikdörtgen', 'Elips', 'Çizgi', 'Ok', 'Metin', 'Numaralı adım', 'Vurgula', 'Bulanıklaştır', 'Pikselleştir', 'Opak kapat', 'Kırp'] as const;

export function CaptureEditorView({ state }: { state: CaptureEditorState }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const historyRef = useRef<ImageData[]>([]);
  const redoRef = useRef<ImageData[]>([]);
  const drawingRef = useRef<{ x: number; y: number } | null>(null);
  const [tool, setTool] = useState(state.initialTool);
  const [color, setColor] = useState('#ff4d5e');
  const [label, setLabel] = useState('Metin');
  const [canUndo, setCanUndo] = useState(false);
  const [canRedo, setCanRedo] = useState(false);
  const [step, setStep] = useState(1);
  const [thickness, setThickness] = useState(4);
  const [zoom, setZoom] = useState(0);
  const [ready, setReady] = useState(false);
  const [dimensions, setDimensions] = useState({ width: 0, height: 0 });
  const [loadError, setLoadError] = useState('');
  const remember = (context: CanvasRenderingContext2D) => {
    historyRef.current.push(context.getImageData(0, 0, context.canvas.width, context.canvas.height));
    // Bound retained full-resolution snapshots by both count and memory.
    while (historyRef.current.length > 1 && (historyRef.current.length > MAX_UNDO_STEPS ||
      historyRef.current.reduce((bytes, image) => bytes + image.data.byteLength, 0) > MAX_HISTORY_BYTES)) historyRef.current.shift();
    redoRef.current = [];
    setCanUndo(true);
    setCanRedo(false);
  };
  const sendImage = (command: string) => {
    const canvas = canvasRef.current;
    if (canvas && ready && !drawingRef.current) window.chrome?.webview?.postMessage({ command, payload: { image: canvas.toDataURL('image/png') } });
  };

  useEffect(() => {
    setReady(false);
    setLoadError('');
    const image = new Image();
    image.onerror = () => setLoadError('Görüntü yüklenemedi. Düzenleyiciyi kapatıp yeniden yakalayın.');
    image.onload = () => {
      const canvas = canvasRef.current;
      const context = canvas?.getContext('2d');
      if (!canvas || !context) return;
      canvas.width = image.naturalWidth;
      canvas.height = image.naturalHeight;
      context.drawImage(image, 0, 0);
      setDimensions({ width: canvas.width, height: canvas.height });
      setReady(true);
      historyRef.current = [];
      redoRef.current = [];
      setCanUndo(false);
      setCanRedo(false);
    };
    image.src = state.image;
    return () => { image.onload = null; image.onerror = null; };
  }, [state.image]);

  const point = (event: React.PointerEvent<HTMLCanvasElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    return { x: Math.max(0, Math.min(event.currentTarget.width - 1, Math.round((event.clientX - rect.left) * event.currentTarget.width / rect.width))), y: Math.max(0, Math.min(event.currentTarget.height - 1, Math.round((event.clientY - rect.top) * event.currentTarget.height / rect.height))) };
  };
  const begin = (event: React.PointerEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    const context = canvas?.getContext('2d');
    if (!canvas || !context || !ready || event.button !== 0) return;
    canvas.closest<HTMLElement>('.feature-root')?.focus({ preventScroll: true });
    drawingRef.current = point(event);
    remember(context);
    canvas.setPointerCapture(event.pointerId);
  };
  const drawMove = (event: React.PointerEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    const context = canvas?.getContext('2d');
    const previous = drawingRef.current;
    if (!canvas || !context || !previous) return;
    if (tool !== Tool.Freehand) {
      const snapshot = historyRef.current.at(-1);
      if (!snapshot) return;
      context.putImageData(snapshot, 0, 0);
      if (tool === Tool.Crop || tool === Tool.Blur || tool === Tool.Pixelate) {
        const end = point(event);
        context.save(); context.strokeStyle = '#fff'; context.lineWidth = 1; context.setLineDash([6, 4]);
        context.strokeRect(previous.x, previous.y, end.x - previous.x, end.y - previous.y); context.restore();
      } else renderShape(context, previous, point(event), false);
      return;
    }
    const next = point(event);
    context.strokeStyle = color; context.lineWidth = thickness; context.lineCap = 'round';
    context.beginPath(); context.moveTo(previous.x, previous.y); context.lineTo(next.x, next.y); context.stroke();
    drawingRef.current = next;
  };
  const finish = (event: React.PointerEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    const context = canvas?.getContext('2d');
    const start = drawingRef.current;
    if (!canvas || !context || !start) return;
    drawingRef.current = null;
    if (tool !== Tool.Freehand) {
      const snapshot = historyRef.current.at(-1);
      if (snapshot) context.putImageData(snapshot, 0, 0);
    }
    renderShape(context, start, point(event), true);
    if (canvas.hasPointerCapture(event.pointerId)) canvas.releasePointerCapture(event.pointerId);
    setDimensions({ width: canvas.width, height: canvas.height });
  };
  const renderShape = (context: CanvasRenderingContext2D, start: Point, end: Point, commit: boolean) => {
    const canvas = context.canvas;
    const left = Math.min(start.x, end.x), top = Math.min(start.y, end.y);
    const width = Math.max(1, Math.abs(end.x - start.x)), height = Math.max(1, Math.abs(end.y - start.y));
    context.strokeStyle = color;
    context.fillStyle = color;
    context.lineWidth = thickness;
    context.lineCap = 'round';
    if (tool === Tool.Freehand) {
      context.beginPath(); context.moveTo(start.x, start.y); context.lineTo(end.x, end.y); context.stroke();
    } else if (tool === Tool.Line || tool === Tool.Arrow) {
      context.beginPath(); context.moveTo(start.x, start.y); context.lineTo(end.x, end.y); context.stroke();
      if (tool === Tool.Arrow) { const angle = Math.atan2(end.y - start.y, end.x - start.x); context.beginPath(); context.moveTo(end.x, end.y); context.lineTo(end.x - 18 * Math.cos(angle - Math.PI / 6), end.y - 18 * Math.sin(angle - Math.PI / 6)); context.lineTo(end.x - 18 * Math.cos(angle + Math.PI / 6), end.y - 18 * Math.sin(angle + Math.PI / 6)); context.closePath(); context.fill(); }
    } else if (tool === Tool.Rectangle || tool === Tool.Highlight || tool === Tool.Redact) {
      context.globalAlpha = tool === Tool.Highlight ? .35 : 1; context.fillStyle = tool === Tool.Highlight ? '#ffd54a' : tool === Tool.Redact ? '#050505' : 'transparent';
      context.fillRect(left, top, width, height); context.globalAlpha = 1;
      if (tool === Tool.Rectangle) context.strokeRect(left, top, width, height);
    } else if (tool === Tool.Ellipse) {
      context.beginPath(); context.ellipse(left + width / 2, top + height / 2, width / 2, height / 2, 0, 0, Math.PI * 2); context.stroke();
    } else if (tool === Tool.Text || tool === Tool.Step) {
      const next = step;
      context.font = `bold ${tool === Tool.Step ? 32 : 24}px Segoe UI`;
      if (tool === Tool.Step) { context.beginPath(); context.arc(start.x, start.y, 20, 0, Math.PI * 2); context.fillStyle = '#fff'; context.fill(); context.strokeStyle = color; context.stroke(); }
      context.fillStyle = color; context.fillText(tool === Tool.Step ? String(next) : label, start.x + (tool === Tool.Step ? -8 : 0), start.y + (tool === Tool.Step ? 10 : 26));
      if (tool === Tool.Step && commit) setStep(next + 1);
    } else if (tool === Tool.Blur || tool === Tool.Pixelate) {
      applyRegionEffect(context, { x: left, y: top, width, height }, tool === Tool.Blur);
    } else if (tool === Tool.Crop && width > 1 && height > 1) {
      const cropped = context.getImageData(left, top, Math.min(width, canvas.width - left), Math.min(height, canvas.height - top));
      canvas.width = cropped.width; canvas.height = cropped.height; canvas.getContext('2d')?.putImageData(cropped, 0, 0);
    }
  };
  const undo = () => {
    const canvas = canvasRef.current;
    const context = canvas?.getContext('2d');
    const previous = historyRef.current.pop();
    if (!canvas || !context || !previous) return;
    redoRef.current.push(context.getImageData(0, 0, canvas.width, canvas.height));
    canvas.width = previous.width; canvas.height = previous.height; context.putImageData(previous, 0, 0); setCanUndo(historyRef.current.length > 0);
    setCanRedo(true);
    setDimensions({ width: canvas.width, height: canvas.height });
  };
  const redo = () => {
    const canvas = canvasRef.current;
    const context = canvas?.getContext('2d');
    const next = redoRef.current.pop();
    if (!canvas || !context || !next) return;
    historyRef.current.push(context.getImageData(0, 0, canvas.width, canvas.height));
    canvas.width = next.width; canvas.height = next.height; context.putImageData(next, 0, 0);
    setCanUndo(true); setCanRedo(redoRef.current.length > 0);
    setDimensions({ width: canvas.width, height: canvas.height });
  };
  const applyPreset = (preset: 'grayscale' | 'sepia' | 'invert') => {
    const canvas = canvasRef.current;
    const context = canvas?.getContext('2d');
    if (!canvas || !context) return;
    remember(context);
    const pixels = context.getImageData(0, 0, canvas.width, canvas.height);
    for (let index = 0; index < pixels.data.length; index += 4) {
      const red = pixels.data[index], green = pixels.data[index + 1], blue = pixels.data[index + 2];
      if (preset === 'grayscale') { const gray = .2126 * red + .7152 * green + .0722 * blue; pixels.data[index] = pixels.data[index + 1] = pixels.data[index + 2] = gray; }
      else if (preset === 'invert') { pixels.data[index] = 255 - red; pixels.data[index + 1] = 255 - green; pixels.data[index + 2] = 255 - blue; }
      else { pixels.data[index] = Math.min(255, .393 * red + .769 * green + .189 * blue); pixels.data[index + 1] = Math.min(255, .349 * red + .686 * green + .168 * blue); pixels.data[index + 2] = Math.min(255, .272 * red + .534 * green + .131 * blue); }
    }
    context.putImageData(pixels, 0, 0); setCanUndo(true); setCanRedo(false);
  };
  const cancelDrawing = () => {
    if (!drawingRef.current) return;
    drawingRef.current = null;
    const previous = historyRef.current.pop();
    const context = canvasRef.current?.getContext('2d');
    if (previous && context) context.putImageData(previous, 0, 0);
    setCanUndo(historyRef.current.length > 0);
  };
  const keyDown = (event: React.KeyboardEvent) => {
    if (event.target instanceof HTMLInputElement || event.target instanceof HTMLSelectElement || event.target instanceof HTMLTextAreaElement) return;
    const key = event.key.toLowerCase();
    if (event.key === 'Escape') {
      if (drawingRef.current) cancelDrawing();
      else window.chrome?.webview?.postMessage({ command: 'capture-editor.close', payload: {} });
    }
    if (!(event.ctrlKey || event.metaKey) || drawingRef.current) return;
    if (key === 'z' || key === 'y') { event.preventDefault(); if (key === 'y' || event.shiftKey) redo(); else undo(); }
    if (key === 'c' || key === 's') { event.preventDefault(); sendImage(key === 'c' ? 'capture-editor.copy' : 'capture-editor.save-file'); }
  };

  return <div className="feature-root feature-root-capture-editor" onKeyDown={keyDown} tabIndex={0}>
    <section className="capture-editor-panel"><header><div><strong>Ekran görüntüsü düzenleyici</strong><small>Çizim, vurgulama, hassas alanları kapatma ve kırpma</small></div><button onClick={() => window.chrome?.webview?.postMessage({ command: 'capture-editor.close', payload: {} })} aria-label="Kapat">×</button></header>
      <fieldset className="capture-editor-toolbar" disabled={!ready}><select value={tool} onChange={event => setTool(Number(event.target.value))} aria-label="Düzenleme aracı">{captureTools.map((name, index) => <option key={name} value={index}>{name}</option>)}</select><input type="color" value={color} onChange={event => setColor(event.target.value)} aria-label="Çizim rengi" /><label>Kalınlık <input type="number" min={1} max={32} value={thickness} onChange={event => setThickness(Math.max(1, Math.min(32, Number(event.target.value) || 1)))} /></label><input value={label} onChange={event => setLabel(event.target.value)} aria-label="Eklenecek metin" /><button disabled={!canUndo} onClick={undo}>Geri al</button><button disabled={!canRedo} onClick={redo}>Yinele</button><button onClick={() => applyPreset('grayscale')}>Siyah-beyaz</button><button onClick={() => applyPreset('sepia')}>Sepya</button><button onClick={() => applyPreset('invert')}>Renkleri ters çevir</button><button onClick={() => sendImage('capture-editor.copy')}>Kopyala</button><button onClick={() => sendImage('capture-editor.save-history')}>Geçmişe kaydet</button><button onClick={() => sendImage('capture-editor.pin')}>Sabitle</button><button onClick={() => sendImage('capture-editor.ocr')}>OCR</button><button onClick={() => sendImage('capture-editor.save-file')}>Dışa aktar</button>{state.workflowCompletion && <button className="primary" onClick={() => sendImage('capture-editor.complete')}>Düzenlemeyi tamamla</button>}</fieldset>
      <div className={`capture-editor-workspace${zoom ? ' zoomed' : ''}`}><canvas style={zoom ? { width: dimensions.width * zoom, height: dimensions.height * zoom } : undefined} ref={canvasRef} onPointerDown={begin} onPointerMove={drawMove} onPointerUp={finish} onPointerCancel={cancelDrawing} onLostPointerCapture={cancelDrawing} /></div>
      <footer><span role="status">{loadError || state.status || `${dimensions.width} × ${dimensions.height} px · Ctrl+Z geri al · Ctrl+Shift+Z yinele · Ctrl+C kopyala · Ctrl+S kaydet`}</span><label>Yakınlaştırma <select aria-label="Yakınlaştırma" value={zoom} onChange={event => setZoom(Number(event.target.value))}><option value={0}>Sığdır</option><option value={0.5}>%50</option><option value={1}>%100</option><option value={2}>%200</option></select></label></footer>
    </section>
  </div>;
}
