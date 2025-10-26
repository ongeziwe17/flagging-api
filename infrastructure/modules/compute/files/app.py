import json

def handler(event, context):
    """Minimal Lambda handler returning health information."""
    return {
        "statusCode": 200,
        "headers": {"Content-Type": "application/json"},
        "body": json.dumps({
            "status": "ok",
            "message": "Feature Flagging API infrastructure baseline"
        }),
    }
