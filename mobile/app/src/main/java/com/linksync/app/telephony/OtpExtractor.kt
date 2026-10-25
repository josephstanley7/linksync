package com.linksync.app.telephony

import java.util.regex.Pattern

object OtpExtractor {
    // High-confidence patterns
    private val PATTERNS = listOf(
        // Google code format: G-123456
        Pattern.compile("(?i)\\b(G-\\d{6})\\b"),
        // "code is 123456", "verification code: 123456", "OTP is 4928"
        Pattern.compile("(?i)\\b(?:code|otp|pin|passcode|token|código)[\\s:]+(?:is\\s+)?(\\d{4,8})\\b"),
        // "Your verification code is 482910"
        Pattern.compile("(?i)\\b(?:verification|security|auth|login)\\s+code\\s+(?:is\\s+)?(\\d{4,8})\\b"),
        // "123456 is your verification code"
        Pattern.compile("(?i)\\b(\\d{4,8})\\s+(?:is\\s+your\\s+(?:verification\\s+code|code|otp|pin|passcode))\\b"),
        // "use 123456 to verify", "enter 123456"
        Pattern.compile("(?i)\\b(?:use|enter|type|input)\\s+(\\d{4,8})\\b"),
        // Generic fallback: any 4-8 digit number if contextual keywords match
        Pattern.compile("\\b(\\d{4,8})\\b")
    )

    private val CONTEXT_KEYWORDS = listOf(
        "verification", "verify", "code", "otp", "pin", "password", "security",
        "auth", "login", "confirm", "código", "banco", "bank", "account"
    )

    private val BLACKLIST_YEARS = setOf("2023", "2024", "2025", "2026", "2027", "2028")

    fun extractOtp(messageBody: String): String? {
        if (messageBody.isBlank()) return null

        val lowerBody = messageBody.lowercase()
        val hasContext = CONTEXT_KEYWORDS.any { lowerBody.contains(it) }

        // 1. Try explicit regexes (first 5 patterns)
        for (i in 0 until 5) {
            val matcher = PATTERNS[i].matcher(messageBody)
            if (matcher.find()) {
                val candidate = matcher.group(1)
                if (isValidCandidate(candidate)) {
                    return candidate
                }
            }
        }

        // 2. Generic fallback if message contains verification context
        if (hasContext) {
            val digitMatcher = PATTERNS[5].matcher(messageBody)
            while (digitMatcher.find()) {
                val candidate = digitMatcher.group(1)
                if (isValidCandidate(candidate)) {
                    return candidate
                }
            }
        }

        return null
    }

    private fun isValidCandidate(candidate: String?): Boolean {
        if (candidate.isNullOrBlank()) return false
        val clean = candidate.trim()
        val length = clean.length
        if (length < 4 || length > 8) return false
        if (BLACKLIST_YEARS.contains(clean)) return false
        return true
    }
}
