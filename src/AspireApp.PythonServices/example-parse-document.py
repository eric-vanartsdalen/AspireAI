import os
from pathlib import Path
from docling.document_converter import DocumentConverter
from docling.datamodel.base_models import InputFormat
from docling.datamodel.pipeline_options import (
    PdfPipelineOptions,
    PictureDescriptionApiOptions
)
from docling.document_converter import DocumentConverter, PdfFormatOption
from docling_core.types.doc.base import ImageRefMode
import gc
import torch
import json
from PIL import Image
import io
from datetime import datetime

from app.services.docling_export_service import export_docling_outputs

def free_torch_cuda_memory():
    # 1) Delete references
    # del variable  # delete any model/tensor references you no longer need
    # 2) Run Python GC to clear unreachable objects
    gc.collect()
    # 3) Release cached memory held by the allocator
    torch.cuda.empty_cache()
    # 4) Release any pending CUDA IPC handles (if using multiprocessing)
    if hasattr(torch.cuda, "ipc_collect"):
        try:
            torch.cuda.ipc_collect()
        except Exception:
            pass
    # 5) Optional: reset peak memory stats (for monitoring)
    try:
        torch.cuda.reset_peak_memory_stats()
    except Exception:
        pass

def extract_and_save_images(doc, output_dir, base_filename):
    """
    Extract images from the Docling document and save them to disk.
    Returns a list of image metadata.
    """
    images_dir = output_dir / "images"
    images_dir.mkdir(exist_ok=True)

    image_metadata = []
    image_counter = 0

    # Access pictures from the document
    if hasattr(doc.document, 'pictures') and doc.document.pictures:
        print(f"Found {len(doc.document.pictures)} pictures in the document")

        for picture in doc.document.pictures:
            try:
                # Get image data
                image_data = picture.get_image(doc.document)

                if image_data:
                    # Generate filename
                    image_filename = f"{base_filename}_image_{image_counter:03d}.png"
                    image_path = images_dir / image_filename

                    # Convert to PIL Image if needed
                    if isinstance(image_data, bytes):
                        pil_image = Image.open(io.BytesIO(image_data))
                    else:
                        pil_image = image_data

                    # Save the image
                    pil_image.save(image_path, "PNG")
                    print(f"Saved image: {image_path}")

                    # Store metadata
                    metadata = {
                        "filename": image_filename,
                        "path": str(image_path),
                        "page": getattr(picture, 'page_no', None),
                        "caption": getattr(picture, 'caption', ''),
                        "description": getattr(picture, 'text', ''),
                        "size": pil_image.size if hasattr(pil_image, 'size') else None
                    }
                    image_metadata.append(metadata)
                    image_counter += 1

            except Exception as e:
                print(f"Error processing image {image_counter}: {e}")
                continue

    return image_metadata

def save_document_outputs(doc, output_dir, base_filename):
    """
    Save the Docling document in multiple formats.
    """
    outputs_dir = output_dir / "outputs"
    saved_files = export_docling_outputs(doc.document, outputs_dir, base_filename)
    for export_name, export_path in saved_files.items():
        print(f"Saved {export_name.upper()}: {export_path}")
    return saved_files

def insert_image_blurbs_in_markdown(markdown_content, image_metadata):
    """
    Insert image blurbs/descriptions into the markdown content.
    This creates a more readable version with image descriptions inline.
    """
    enhanced_markdown = markdown_content

    for i, img_meta in enumerate(image_metadata):
        # Create a blurb with image description
        blurb = f"\n\n---\n**Image {i+1}:** {img_meta.get('filename', 'Unknown')}\n"
        if img_meta.get('caption'):
            blurb += f"**Caption:** {img_meta['caption']}\n"
        if img_meta.get('description'):
            blurb += f"**Description:** {img_meta['description']}\n"
        if img_meta.get('page'):
            blurb += f"**Page:** {img_meta['page']}\n"
        blurb += "---\n\n"

        # Find a good place to insert the blurb (after the image reference in markdown)
        # This is a simple approach - you might want to make this more sophisticated
        image_ref = f"![{img_meta.get('filename', f'image_{i}')}]"
        if image_ref in enhanced_markdown:
            enhanced_markdown = enhanced_markdown.replace(image_ref, image_ref + blurb)
        else:
            # If no image reference found, append at the end
            enhanced_markdown += blurb

    return enhanced_markdown

def convert_with_image_annotation(input_doc_path):
    free_torch_cuda_memory()
    # You need to have Ollama running locally with CUDA enabled for GPU acceleration.
    # Start Ollama with CUDA support (ollama run <model> --cuda or ollama serve --cuda)
    # The endpoint below is the default for Ollama.
    from pydantic import AnyUrl, ValidationError
    # model = "llava:latest"  # Use a vision-capable Ollama model
    # model = "llava-phi3:latest"
    # model = "qwen3-vl:latest"
    # model = "gemma3:latest"
    model = "gemma3-2b:latest"
    params = dict(model=model)
    params["device"] = "cuda"  # This may be ignored if Ollama does not support it in API
    ollama_url_str = os.environ.get("OLLAMA_URL", "http://127.0.0.1:11434/v1/chat/completions")
    try:
        ollama_url = AnyUrl(ollama_url_str)
    except ValidationError:
        raise ValueError(f"OLLAMA_URL '{ollama_url_str}' is not a valid URL for AnyUrl.")
    picture_desc_api_option = PictureDescriptionApiOptions(
        url=ollama_url,
        prompt="Describe this image in sentences in a single paragraph.",
        params=params,
        headers={},
        timeout=600,
    )
    # Log device info for debugging
    import logging
    if not torch.cuda.is_available():
        logging.warning("CUDA is not available. The pipeline will run on CPU. Ensure Ollama is started with CUDA support and your GPU drivers are installed.")
    else:
        logging.info(f"CUDA is available. Using device: {torch.cuda.get_device_name(torch.cuda.current_device())}")
    pipeline_options = PdfPipelineOptions(
        do_picture_description=True,
        picture_description_options=picture_desc_api_option,
        enable_remote_services=True,
        generate_picture_images=True,
        images_scale=2,
    )
    converter = DocumentConverter(
        format_options={InputFormat.PDF: PdfFormatOption(pipeline_options=pipeline_options)}
    )
    conv_res = converter.convert(source=input_doc_path)
    free_torch_cuda_memory()
    return conv_res

if __name__ == "__main__":
    # Start timing
    start_time = datetime.now()
    print(f"Processing started at: {start_time.strftime('%Y-%m-%d %H:%M:%S')}")

    # Get the absolute path of the current script
    script_path = Path(__file__).resolve()
    # Get the directory containing the script
    script_dir = script_path.parent
    print(f"Script directory: {script_dir}")

    # Target pdf
    target_pdf = script_dir / "Data Science from Scratch by Joel Grus.pdf"
    print(f"Target PDF: {target_pdf}")

    # Create output directory
    base_filename = target_pdf.stem  # Get filename without extension
    output_dir = script_dir / f"output_{base_filename}"
    output_dir.mkdir(exist_ok=True)
    print(f"Output directory: {output_dir}")

    # conversion with image annotation
    print("Converting document with image annotation...")
    doc = convert_with_image_annotation(target_pdf)
    print(f"Document has {len(doc.document.pages)} pages.")

    # Extract and save images
    print("\nExtracting and saving images...")
    image_metadata = extract_and_save_images(doc, output_dir, base_filename)

    # Save document in multiple formats
    print("\nSaving document outputs...")
    saved_files = save_document_outputs(doc, output_dir, base_filename)

    # Create enhanced markdown with image blurbs
    print("\nCreating enhanced markdown with image blurbs...")
    original_markdown = doc.document.export_to_markdown()
    enhanced_markdown = insert_image_blurbs_in_markdown(original_markdown, image_metadata)

    # Save enhanced markdown
    enhanced_md_path = output_dir / "outputs" / f"{base_filename}_with_blurbs.md"
    with open(enhanced_md_path, 'w', encoding='utf-8') as f:
        f.write(enhanced_markdown)
    print(f"Saved enhanced markdown with blurbs: {enhanced_md_path}")

    # End timing and calculate duration
    end_time = datetime.now()
    duration = end_time - start_time
    duration_seconds = duration.total_seconds()

    print(f"\nProcessing completed at: {end_time.strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"Total processing time: {duration_seconds:.2f} seconds ({duration})")

    # Save processing metadata
    metadata = {
        "source_file": str(target_pdf),
        "processing_date": datetime.now().isoformat(),
        "start_time": start_time.isoformat(),
        "end_time": end_time.isoformat(),
        "duration_seconds": duration_seconds,
        "duration_formatted": str(duration),
        "pages": len(doc.document.pages),
        "images_extracted": len(image_metadata),
        "output_formats": list(saved_files.keys()),
        "saved_files": saved_files,
        "image_metadata": image_metadata,
        "cuda_available": torch.cuda.is_available(),
        "cuda_device": torch.cuda.get_device_name(torch.cuda.current_device()) if torch.cuda.is_available() else None
    }

    metadata_path = output_dir / "processing_metadata.json"
    with open(metadata_path, 'w', encoding='utf-8') as f:
        json.dump(metadata, f, indent=2, ensure_ascii=False)
    print(f"Saved processing metadata: {metadata_path}")

    # Print summary
    print("\n=== Processing Summary ===")
    print(f"Source: {target_pdf.name}")
    print(f"Pages: {len(doc.document.pages)}")
    print(f"Images extracted: {len(image_metadata)}")
    print(f"Output directory: {output_dir}")
    print(f"Formats saved: {', '.join(saved_files.keys())}")
    print(f"Total processing time: {duration_seconds:.2f} seconds")

    print("\nFiles created:")
    for format_name, file_path in saved_files.items():
        print(f"  - {format_name.upper()}: {Path(file_path).name}")
    print(f"  - Enhanced Markdown: {Path(enhanced_md_path).name}")
    print(f"  - Metadata: {Path(metadata_path).name}")

    if image_metadata:
        print(f"\nImages saved to: {output_dir}/images/")
        for img in image_metadata:
            print(f"  - {img['filename']} (Page {img.get('page', 'N/A')})")

    print(f"\nOriginal markdown preview (first 500 chars):")
    print(doc.document.export_to_markdown()[:500])

