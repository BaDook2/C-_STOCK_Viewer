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

// Inspect bounds only, without capturing screen contents or reading titles.
// Excluding desktop elements makes Finder's wallpaper and icons eligible while
// clicks on application windows, menus and the Dock remain untouched.
static BOOL SVIsDesktop(CGPoint point) {
    NSArray *windows = CFBridgingRelease(CGWindowListCopyWindowInfo(
        kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements, kCGNullWindowID));
    if (windows == nil) return NO;
    for (NSDictionary *window in windows) {
        NSNumber *alpha = window[(id)kCGWindowAlpha];
        if (alpha != nil && alpha.doubleValue <= 0) continue;
        CGRect bounds;
        if (CGRectMakeWithDictionaryRepresentation((__bridge CFDictionaryRef)window[(id)kCGWindowBounds], &bounds)
            && CGRectContainsPoint(bounds, point)) return NO;
    }
    return YES;
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
                current.tracking = SVIsDesktop(point);
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
