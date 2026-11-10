#import <Cocoa/Cocoa.h>
#import <CoreGraphics/CoreGraphics.h>

typedef void (*SVDesktopCallback)(int, double, double, int);

@interface SVDesktopObserver : NSObject
@property(nonatomic, strong) id token;
@property(nonatomic) SVDesktopCallback callback;
@property(nonatomic) BOOL tracking;
@end
@implementation SVDesktopObserver
@end

// Ask AppKit where a click actually lands. A rectangle-only scan mistakes the
// Dock's screen-sized, transparent, click-through window for an obstruction.
// This API skips transparent pixels and windows that ignore mouse events.
int sv_desktop_is_desktop(double x, double y) {
    NSScreen *primary = NSScreen.screens.firstObject;
    if (primary == nil) return NO;
    NSInteger hit = [NSWindow windowNumberAtPoint:NSMakePoint(x, NSMaxY(primary.frame) - y)
                    belowWindowWithWindowNumber:0];
    if (hit <= 0) return NO;
    NSArray *windows = CFBridgingRelease(CGWindowListCopyWindowInfo(
        kCGWindowListOptionIncludingWindow, (CGWindowID)hit));
    NSDictionary *window = windows.firstObject;
    if (window == nil) return NO;
    pid_t pid = [window[(id)kCGWindowOwnerPID] intValue];
    NSString *bundle = [NSRunningApplication runningApplicationWithProcessIdentifier:pid].bundleIdentifier;
    NSInteger level = [window[(id)kCGWindowLayer] integerValue];
    BOOL desktopOwner = [bundle isEqualToString:@"com.apple.finder"]
        || [bundle isEqualToString:@"com.apple.dock"] || [bundle isEqualToString:@"com.apple.WindowManager"]
        || [bundle isEqualToString:@"com.apple.wallpaper.agent"];
    // Normal Finder file windows, the Dock bar, menus and desktop widgets must
    // keep their own gestures. Only the system's background layers qualify.
    return desktopOwner && level < 0;
}

void *sv_desktop_start(SVDesktopCallback callback) {
    SVDesktopObserver *observer = [SVDesktopObserver new];
    observer.callback = callback;
    __weak SVDesktopObserver *weakObserver = observer;
    observer.token = [NSEvent addGlobalMonitorForEventsMatchingMask:
        (NSEventMaskLeftMouseDown | NSEventMaskLeftMouseDragged | NSEventMaskLeftMouseUp)
        handler:^(NSEvent *event) {
            SVDesktopObserver *current = weakObserver;
            if (current == nil || event.CGEvent == NULL) return;
            CGPoint point = CGEventGetLocation(event.CGEvent);
            if (event.type == NSEventTypeLeftMouseDown) {
                current.tracking = sv_desktop_is_desktop(point.x, point.y);
                if (current.tracking) current.callback(1, point.x, point.y,
                    (event.modifierFlags & (NSEventModifierFlagControl | NSEventModifierFlagCommand)) != 0);
            } else if (current.tracking) {
                if (event.type == NSEventTypeLeftMouseUp) {
                    current.tracking = NO;
                    current.callback(3, point.x, point.y, 0);
                } else current.callback(2, point.x, point.y, 0);
            }
        }];
    return observer.token == nil ? NULL : (__bridge_retained void *)observer;
}

void sv_desktop_stop(void *handle) {
    if (handle == NULL) return;
    SVDesktopObserver *observer = (__bridge_transfer SVDesktopObserver *)handle;
    [NSEvent removeMonitor:observer.token];
    observer.token = nil;
    observer.tracking = NO;
}
