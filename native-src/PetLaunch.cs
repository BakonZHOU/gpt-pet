using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CodexPetPlugin;

public sealed partial class PetWindow
{
    private enum LaunchPhase { Idle, Flying, Falling, Resting, Returning }
    private LaunchPhase launchPhase;
    private readonly DispatcherTimer launchTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch launchClock = new();
    private readonly Stopwatch holdClock = new();
    private readonly RotateTransform flightRotation = new();
    private readonly ScaleTransform flightSquash = new(1, 1);
    private readonly RotateTransform aimRotation = new();
    private readonly Canvas chargeArrow = new() { Width = 320, Height = 320, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Popup arrowPopup = new() { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.AbsolutePoint, IsHitTestVisible = false };
    private readonly Viewbox arrowView = new() { IsHitTestVisible = false };
    private FrameworkElement launchPet = null!;
    private Vector velocity;
    private double spin, aim, previousFrame, restAt, returnAt, returnLeft, returnTop, returnAngle;
    private int impacts;
    private double landingGravity;
    private Rect flightBounds;
    private double cachedFloor, nextBoundsRead;
    private bool ChargeReady => holdClock.IsRunning && holdClock.Elapsed.TotalSeconds >= 1.22;

    private void SetupLaunch(FrameworkElement pet)
    {
        launchPet = pet;
        breathe.CenterY = pet.Height / 2;
        var transforms = new TransformGroup();
        transforms.Children.Add(breathe); transforms.Children.Add(flightSquash); transforms.Children.Add(flightRotation);
        pet.RenderTransform = transforms; pet.RenderTransformOrigin = new Point(.5, .5);
        var arrowInk = new SolidColorBrush(Color.FromRgb(207, 89, 103)); arrowInk.Freeze();
        var shaft = new System.Windows.Shapes.Path { Data = Geometry.Parse("M160,160 L291,160"), Stroke = arrowInk,
            StrokeThickness = 5, StrokeDashArray = new DoubleCollection { 1.3, 1.7 }, StrokeDashCap = PenLineCap.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var head = new System.Windows.Shapes.Path { Data = Geometry.Parse("M295,149 Q301,154 310,160 Q301,166 295,171"),
            Stroke = arrowInk, StrokeThickness = 5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        chargeArrow.Children.Add(shaft); chargeArrow.Children.Add(head);
        aimRotation.CenterX = aimRotation.CenterY = 160; chargeArrow.RenderTransform = aimRotation;
        arrowView.Child = chargeArrow; arrowPopup.Child = arrowView;
        launchTimer.Tick += (_, _) => TickLaunch();
    }
    private void BeginCharge()
    {
        holdClock.Restart(); launchTimer.Start();
    }
    private void CancelCharge()
    {
        holdClock.Reset(); chargeArrow.Visibility = Visibility.Collapsed;
        arrowPopup.IsOpen = false;
        if (launchPhase == LaunchPhase.Idle) launchTimer.Stop();
    }
    private void TickLaunch()
    {
        if (launchPhase == LaunchPhase.Idle)
        {
            if (!ChargeReady) return;
            StopAlerts(); HideBubble(true); menu.IsOpen = false;
            double energy = Math.Clamp((holdClock.Elapsed.TotalSeconds - 1.22) / 13.78, 0, 1);
            squeeze.BeginAnimation(ScaleTransform.ScaleXProperty, null); squeeze.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            squeeze.ScaleX = 1.05 - energy * .46; squeeze.ScaleY = .88 - energy * .4;
            var cursor = CursorPosition();
            Point chest = launchPet.PointToScreen(new Point(launchPet.ActualWidth / 2, launchPet.ActualHeight * .85));
            aim = Math.Atan2(cursor.Y - chest.Y, cursor.X - chest.X);
            chargeArrow.Visibility = Visibility.Visible;
            double arrowScale = Width / Base;
            arrowView.Width = arrowView.Height = 320 * arrowScale;
            var dpi = VisualTreeHelper.GetDpi(this);
            arrowPopup.HorizontalOffset = chest.X / dpi.DpiScaleX - 160 * arrowScale;
            arrowPopup.VerticalOffset = chest.Y / dpi.DpiScaleY - 160 * arrowScale;
            aimRotation.Angle = aim * 180 / Math.PI; arrowPopup.IsOpen = true;
            return;
        }
        double elapsed = launchClock.Elapsed.TotalSeconds;
        double delta = Math.Min(.04, elapsed - previousFrame); previousFrame = elapsed;
        Rect bounds = flightBounds;
        if (elapsed >= nextBoundsRead) { cachedFloor = TaskbarAvoidance.Read(this).AvailableBottom; nextBoundsRead = elapsed + .15; }
        double floorBottom = cachedFloor;
        double size = Width * WhaleFraction;
        double inset = Width - size;
        double radians = flightRotation.Angle * Math.PI / 180;
        double edgeInset = size * .5 * (1 - .7 * (Math.Abs(Math.Cos(radians)) + Math.Abs(Math.Sin(radians))));
        double leftEdge = bounds.Left - inset - edgeInset;
        double rightEdge = Math.Max(leftEdge, bounds.Right - Width + edgeInset);
        double floor = floorBottom - Height + edgeInset;
        if (launchPhase == LaunchPhase.Flying)
        {
            // Substeps keep fast launches from tunnelling through screen edges.
            int steps = Math.Max(1, (int)Math.Ceiling(delta / .004));
            double frameX = Left, frameY = Top;
            for (int i = 0; i < steps; i++)
            {
                double dt = delta / steps;
                velocity.Y += 1500 * dt;
                velocity *= Math.Exp(-.28 * dt);
                double x = frameX + velocity.X * dt, y = frameY + velocity.Y * dt;
                bool hitX = false, hitY = false;
                double impactX = Math.Abs(velocity.X), impactY = Math.Abs(velocity.Y);
                double minX = leftEdge, maxX = rightEdge;
                double minY = bounds.Top - inset - edgeInset, maxY = Math.Max(minY, floor);
                if (x < minX || x > maxX) { x = Math.Clamp(x, minX, Math.Max(minX,maxX)); velocity.X *= -.8; hitX = true; }
                if (y < minY || y > maxY) { y = Math.Clamp(y, minY, maxY); velocity.Y *= -.74; hitY = true; }
                frameX = x; frameY = y;
                if (hitX || hitY) { impacts++; spin *= .8; if(hitX) ImpactSquash(true, impactX); if(hitY) ImpactSquash(false, impactY); }
                if (hitY && y >= maxY - .1 && velocity.Length < 180)
                { LandPet(maxY, elapsed); break; }
                if (elapsed >= 4.2 || impacts >= 8)
                {
                    // Spend the final fraction of flight actually falling, rather than snapping to the floor.
                    launchPhase = LaunchPhase.Falling; velocity.Y = Math.Max(0, velocity.Y);
                    landingGravity = Math.Max(1500, 2 * Math.Max(0, maxY - frameY) / (.55 * .55));
                    break;
                }
            }
            Left = frameX; Top = frameY;
            flightRotation.Angle += spin * delta; spin *= Math.Exp(-.24 * delta);
        }
        else if (launchPhase == LaunchPhase.Falling)
        {
            velocity.Y += landingGravity * delta; velocity.X *= Math.Exp(-6 * delta); spin *= Math.Exp(-3 * delta);
            Left = Math.Clamp(Left + velocity.X * delta, leftEdge, rightEdge);
            Top = Math.Min(floor, Top + velocity.Y * delta);
            flightRotation.Angle += spin * delta;
            if (Top >= floor - .1) { ImpactSquash(false, Math.Abs(velocity.Y)); LandPet(floor, elapsed); }
        }
        else if (launchPhase == LaunchPhase.Resting && elapsed - restAt >= 2)
        {
            launchPhase = LaunchPhase.Returning; returnAt = elapsed;
            returnLeft = Left; returnTop = Top; returnAngle = flightRotation.Angle % 360;
            if (returnAngle > 180) returnAngle -= 360;
        }
        else if (launchPhase == LaunchPhase.Returning)
        {
            double t = Math.Clamp((elapsed - returnAt) / .65, 0, 1), ease = t*t*(3-2*t);
            Left = returnLeft + (bounds.Right - Width - returnLeft) * ease;
            Top = returnTop + (floorBottom - Height - returnTop) * ease;
            flightRotation.Angle = returnAngle * (1-ease);
            flightSquash.ScaleX = flightSquash.ScaleY = .7 + .3*ease;
            if (t >= 1)
            {
                launchPhase = LaunchPhase.Idle; launchTimer.Stop(); CompositionTarget.Rendering -= RenderLaunch; flightRotation.Angle = 0;
                flightSquash.ScaleX = flightSquash.ScaleY = 1; bottomGap = 0; taskbarTarget = null;
                settings.Left = Left; settings.Top = Top; settings.BottomGap = 0; settings.Save(); SetAnimation();
            }
        }
    }
    private void FirePet(double? testEnergy = null)
    {
        double energy = testEnergy ?? Math.Clamp((holdClock.Elapsed.TotalSeconds - 1.22) / 13.78, 0, 1);
        CancelCharge(); StopSounds(); HideBubble(true); bottomGap = null; menuButton.Opacity = 0;
        settings.Mirror = false; ApplyMirror(false);
        squeeze.BeginAnimation(ScaleTransform.ScaleXProperty, null); squeeze.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        squeeze.ScaleX = squeeze.ScaleY = 1;
        breathe.BeginAnimation(ScaleTransform.ScaleXProperty, null); breathe.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        breathe.ScaleX = breathe.ScaleY = 1;
        flightSquash.ScaleX = flightSquash.ScaleY = .7;
        double speed = 650 + energy * 4500;
        velocity = new Vector(Math.Cos(aim)*speed, Math.Sin(aim)*speed);
        spin = (Random.Shared.Next(2)==0 ? -1 : 1) * (180 + energy*750);
        flightBounds = SystemParameters.WorkArea; cachedFloor = TaskbarAvoidance.Read(this).AvailableBottom; nextBoundsRead = .15;
        impacts = 0; previousFrame = 0; launchPhase = LaunchPhase.Flying; launchClock.Restart(); launchTimer.Stop();
        CompositionTarget.Rendering -= RenderLaunch; CompositionTarget.Rendering += RenderLaunch;
    }
    private void RenderLaunch(object? sender, EventArgs args) => TickLaunch();
    private void ImpactSquash(bool horizontal, double normalSpeed)
    {
        double force = Math.Clamp((normalSpeed - 70) / 3600, 0, 1);
        if (force <= 0) return;
        // The rotation changes which local axis meets the wall.
        bool localX = horizontal == (Math.Abs(Math.Cos(flightRotation.Angle*Math.PI/180)) > .707);
        double compressed = .7 * (1 - .3 * force), stretched = .7 / Math.Sqrt(1 - .3 * force);
        foreach (var (property, from) in new[] { (ScaleTransform.ScaleXProperty, localX ? compressed : stretched), (ScaleTransform.ScaleYProperty, localX ? stretched : compressed) })
            flightSquash.BeginAnimation(property, new DoubleAnimation(from, .7, TimeSpan.FromSeconds(.08 + .08 * force)) { FillBehavior = FillBehavior.Stop });
    }
    private void LandPet(double floor, double elapsed)
    {
        Top = floor; velocity = new(); spin = 0; restAt = elapsed; launchPhase = LaunchPhase.Resting;
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    private static Point CursorPosition() { GetCursorPos(out var point); return new(point.X, point.Y); }
}
