"""Basic claim extraction service for Phase 2."""

import re
from typing import List, Dict, Any


class ClaimExtractionService:
    """Extract basic claims from page content.
    
    Phase 2 implementation: Simple sentence-based extraction.
    Future: LLM-powered extraction with semantic understanding.
    """
    
    def __init__(self, min_claim_length: int = 20, max_claim_length: int = 500):
        self.min_claim_length = min_claim_length
        self.max_claim_length = max_claim_length
    
    def extract_claims(
        self, 
        content: str, 
        source_confidence: float = 0.7,
        source_type: str = "extraction"
    ) -> List[Dict[str, Any]]:
        """Extract claims from text content.
        
        Args:
            content: Page or chunk content to extract claims from
            source_confidence: Base confidence from document source
            source_type: Type of extraction (e.g., "extraction", "manual")
        
        Returns:
            List of claim dictionaries with text, confidence, metadata
        """
        if not content or not content.strip():
            return []
        
        # Split into sentences (basic approach)
        sentences = self._split_sentences(content)
        
        claims = []
        for sentence in sentences:
            # Filter by length
            if len(sentence) < self.min_claim_length or len(sentence) > self.max_claim_length:
                continue
            
            # Basic quality heuristics
            claim_confidence = self._estimate_confidence(sentence, source_confidence)
            
            claims.append({
                "text": sentence.strip(),
                "confidence": claim_confidence,
                "source_type": source_type,
                "metadata": {
                    "extraction_method": "sentence_split",
                    "sentence_length": len(sentence),
                    "source_confidence": source_confidence
                }
            })
        
        return claims
    
    def _split_sentences(self, content: str) -> List[str]:
        """Split content into sentences using basic punctuation rules."""
        # Simple sentence splitting (not perfect but good enough for Phase 2)
        # Split on period, exclamation, question mark followed by space or end
        sentences = re.split(r'[.!?]+\s+', content)
        return [s for s in sentences if s.strip()]
    
    def _estimate_confidence(self, sentence: str, source_confidence: float) -> float:
        """Estimate claim confidence based on sentence characteristics.
        
        Phase 2: Simple heuristics based on length and completeness.
        Future: LLM-based confidence scoring.
        """
        confidence = source_confidence
        
        # Penalize very short sentences (likely fragments)
        if len(sentence) < 30:
            confidence *= 0.8
        
        # Penalize incomplete sentences (basic check)
        if not self._looks_complete(sentence):
            confidence *= 0.7
        
        # Boost longer, detailed sentences
        if len(sentence) > 100:
            confidence = min(1.0, confidence * 1.1)
        
        return round(confidence, 3)
    
    def _looks_complete(self, sentence: str) -> bool:
        """Check if sentence appears complete (basic heuristics)."""
        sentence = sentence.strip()
        
        # Must have at least one verb indicator (very basic)
        verb_indicators = [' is ', ' are ', ' was ', ' were ', ' has ', ' have ', ' had ', ' can ', ' will ', ' would ']
        has_verb = any(indicator in sentence.lower() for indicator in verb_indicators)
        
        # Should start with capital letter
        starts_proper = sentence[0].isupper() if sentence else False
        
        return has_verb and starts_proper
